const express = require('express');
const cors = require('cors');
const dotenv = require('dotenv');
const axios = require('axios');
const { GoogleGenAI, Type } = require('@google/genai');

dotenv.config();

const app = express();
const port = process.env.PORT || 5051;
const catalogApiBase = process.env.CATALOG_API_BASE || 'http://163.13.202.116:5050';
const ai = new GoogleGenAI({ apiKey: process.env.GEMINI_API_KEY });

app.use(cors());
app.use(express.json({ limit: '200kb' }));
app.use(express.static('public'));

const EMPTY_FILTERS = Object.freeze({
    category: '', color: '', material: '', maxPrice: 0, roomAreaPing: 0, style: ''
});

let furnitureCatalog = null;
let furnitureOptions = null;
let catalogLoading = null;

const cleanText = value => typeof value === 'string' ? value.trim() : '';
const cleanNumber = value => {
    const number = Number(value);
    return Number.isFinite(number) && number > 0 ? number : 0;
};

function normalizeFilters(value = EMPTY_FILTERS) {
    return {
        category: cleanText(value.category),
        color: cleanText(value.color),
        material: cleanText(value.material),
        maxPrice: cleanNumber(value.maxPrice),
        roomAreaPing: cleanNumber(value.roomAreaPing),
        style: cleanText(value.style)
    };
}

function distinctValues(items, field) {
    return [...new Set(items.map(item => cleanText(item[field])).filter(Boolean))].sort();
}

async function loadFurnitureCatalog() {
    console.time('載入家具目錄');
    try {
        const response = await axios.get(`${catalogApiBase}/api/furnitures`, { timeout: 15000 });
        const items = Array.isArray(response.data) ? response.data : response.data?.data;
        if (!Array.isArray(items)) throw new Error('家具 API 回傳格式不是陣列');

        furnitureCatalog = items;
        furnitureOptions = {
            categories: distinctValues(items, 'category'),
            colors: distinctValues(items, 'color'),
            materials: distinctValues(items, 'material')
        };
        console.log(`家具目錄載入完成，共 ${items.length} 筆；重啟服務前不再重複讀取資料庫。`);
        return items;
    } finally {
        console.timeEnd('載入家具目錄');
    }
}

async function getFurnitureCatalog() {
    if (furnitureCatalog) return furnitureCatalog;
    if (!catalogLoading) {
        catalogLoading = loadFurnitureCatalog()
            .catch(error => {
                console.error('家具目錄載入失敗:', error.message);
                return null;
            })
            .finally(() => { catalogLoading = null; });
    }
    return catalogLoading;
}

function sanitizeHistory(history) {
    if (!Array.isArray(history)) return [];
    return history.slice(-10).map(item => ({
        role: item?.role === 'model' || item?.role === 'ai' ? '助理' : '使用者',
        text: cleanText(item?.text).slice(0, 500)
    })).filter(item => item.text);
}

function includesNormalized(source, expected) {
    if (!expected) return true;
    const left = cleanText(source).toLocaleLowerCase('zh-TW');
    const right = cleanText(expected).toLocaleLowerCase('zh-TW');
    return left.includes(right) || right.includes(left);
}

function matchesHardFilters(item, filters) {
    if (!includesNormalized(item.category, filters.category)) return false;
    if (!includesNormalized(item.color, filters.color)) return false;
    if (!includesNormalized(item.material, filters.material)) return false;
    return !(filters.maxPrice > 0 && Number(item.price) > filters.maxPrice);
}

const formatOptions = values => values.length ? values.join('、') : '目前沒有資料';

app.post('/api/chat', async (req, res) => {
    try {
        const message = cleanText(req.body?.message);
        if (!message) return res.status(400).json({ error: '請輸入訊息' });

        const catalog = await getFurnitureCatalog();
        if (!catalog) {
            return res.status(503).json({ error: '家具資料暫時無法載入，請確認家具 API 後再試。' });
        }

        const previousFilters = normalizeFilters(req.body?.filters);
        const history = sanitizeHistory(req.body?.history);
        const historyText = history.length
            ? history.map(item => `${item.role}：${item.text}`).join('\n')
            : '無先前對話';
        const aiKnowledgeBase = catalog.map(f => (
            `ID: ${f.id} | 名稱: ${f.name} | 類別: ${f.category || ''}` +
            ` | 顏色: ${f.color || ''} | 材質: ${f.material || ''}` +
            ` | 價格: ${Number(f.price) || 0}元` +
            ` | 尺寸: ${f.width || 0}x${f.length_cm || 0}x${f.height || 0}cm` +
            ` | 描述: ${f.description || ''}`
        )).join('\n');

        const systemInstruction = `
你是專業的室內設計助理，必須延續多輪對話中的選購條件。
資料庫允許的類別：${formatOptions(furnitureOptions.categories)}
資料庫允許的顏色：${formatOptions(furnitureOptions.colors)}
資料庫允許的材質：${formatOptions(furnitureOptions.materials)}
上一輪累積條件：${JSON.stringify(previousFilters)}
最近對話：\n${historyText}
真實家具清單：\n${aiKnowledgeBase}

規則：
1. 根據最新訊息更新 filters；沒有改動的條件必須沿用上一輪。
2. 使用者說不限、取消或都可以時，對應文字欄位回傳空字串，數字欄位回傳 0。
3. category、color、material 優先使用資料庫實際值；無法確認時用空字串。
4. category、color、material、maxPrice 是硬條件，推薦項目必須全部符合。
5. roomAreaPing、style 是軟偏好，用於尺寸與風格排序，不是絕對排除條件。
6. recommendations 只能包含真實整數 ID，最多 8 筆；沒有符合項目時回傳空陣列。
7. reply 要說明沿用了哪些條件；若沒有符合項目，指出可以放寬的條件。
8. 只回傳指定 JSON 結構。`;

        console.time('Gemini 回應');
        const aiResponse = await ai.models.generateContent({
            model: 'gemini-2.5-flash',
            contents: message,
            config: {
                systemInstruction,
                temperature: 0.2,
                responseMimeType: 'application/json',
                responseSchema: {
                    type: Type.OBJECT,
                    properties: {
                        reply: { type: Type.STRING },
                        recommendations: { type: Type.ARRAY, items: { type: Type.INTEGER } },
                        filters: {
                            type: Type.OBJECT,
                            properties: {
                                category: { type: Type.STRING }, color: { type: Type.STRING },
                                material: { type: Type.STRING }, maxPrice: { type: Type.NUMBER },
                                roomAreaPing: { type: Type.NUMBER }, style: { type: Type.STRING }
                            },
                            required: ['category', 'color', 'material', 'maxPrice', 'roomAreaPing', 'style']
                        }
                    },
                    required: ['reply', 'recommendations', 'filters']
                }
            }
        });
        console.timeEnd('Gemini 回應');

        let aiResult;
        try {
            aiResult = JSON.parse(aiResponse.text.trim());
        } catch (error) {
            console.error('AI 回傳的不是合法 JSON:', aiResponse.text.trim());
            return res.status(502).json({ error: 'AI 回應格式異常，請再試一次。' });
        }

        const filters = normalizeFilters(aiResult.filters);
        const catalogById = new Map(catalog.map(item => [Number(item.id), item]));
        const recommendations = [...new Set(
            (Array.isArray(aiResult.recommendations) ? aiResult.recommendations : [])
                .map(Number)
                .filter(Number.isInteger)
                .filter(id => {
                    const item = catalogById.get(id);
                    return item && matchesHardFilters(item, filters);
                })
        )].slice(0, 8);

        res.json({
            reply: cleanText(aiResult.reply) || '這次沒有取得有效的推薦說明。',
            recommendations,
            filters
        });
    } catch (error) {
        console.error('AI 或網路處理出錯:', error);
        res.status(500).json({ error: 'AI 伺服器發生錯誤。' });
    }
});

app.listen(port, () => {
    console.log(`AI 模擬伺服器已啟動，port：${port}`);
    getFurnitureCatalog();
});
