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
const MAX_AI_CANDIDATES = Math.min(
    Math.max(Number(process.env.MAX_AI_CANDIDATES) || 40, 10),
    100
);

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

// 將常見中文數字轉成阿拉伯數字，讓「兩千元」也能在呼叫 AI 前先篩選。
function parseChineseNumber(text) {
    if (!text) return 0;
    if (/^[\d,]+$/.test(text)) return Number(text.replaceAll(',', '')) || 0;

    const digits = { 零: 0, 〇: 0, 一: 1, 二: 2, 兩: 2, 三: 3, 四: 4, 五: 5, 六: 6, 七: 7, 八: 8, 九: 9 };
    const units = { 十: 10, 百: 100, 千: 1000 };
    let total = 0;
    let section = 0;
    let number = 0;

    for (const char of text) {
        if (Object.prototype.hasOwnProperty.call(digits, char)) {
            number = digits[char];
        } else if (Object.prototype.hasOwnProperty.call(units, char)) {
            section += (number || 1) * units[char];
            number = 0;
        } else if (char === '萬') {
            total += (section + number || 1) * 10000;
            section = 0;
            number = 0;
        }
    }
    return total + section + number;
}

// 從最新一句話找出明確預算；找不到時保留上一輪預算，交由 Gemini 理解模糊說法。
function extractMaxPrice(message) {
    const numberPattern = '[\\d,]+|[零〇一二兩三四五六七八九十百千萬]+';
    const withPriceWord = new RegExp(`(?:預算|價格|售價|最多|上限|改成|調整為|提高到|降到)[^\\d零〇一二兩三四五六七八九十百千萬]{0,8}(${numberPattern})\\s*(?:元|塊)?`);
    const withLimitWord = new RegExp(`(${numberPattern})\\s*(?:元|塊)?\\s*(?:以下|以內|內)`);
    const match = message.match(withPriceWord) || message.match(withLimitWord);
    return match ? parseChineseNumber(match[1]) : 0;
}

// 優先比對資料庫的完整選項，再以常見關鍵字對應實際的類別、顏色與材質名稱。
function findMentionedOption(message, options, aliases) {
    const directMatches = options
        .filter(option => message.includes(option))
        .sort((a, b) => message.lastIndexOf(b) - message.lastIndexOf(a));
    if (directMatches.length) return directMatches[0];

    for (const alias of aliases) {
        if (!message.includes(alias)) continue;
        const option = options.find(value => value.includes(alias));
        if (option) return option;
    }
    return '';
}

// 將口語同義詞換成資料庫與規則較容易辨識的說法，不要求使用者輸入完全相同的字。
function normalizeSearchTerms(message) {
    const replacements = [
        [/座椅|凳子/g, '椅子'],
        [/木頭|木質/g, '木'],
        [/真皮|皮製/g, '皮革'],
        [/布製|布質/g, '布料'],
        [/鐵製|鋼製/g, '金屬'],
        [/暗色系|深色系/g, '黑色'],
        [/亮色系|淺色系/g, '白色'],
        [/物色|挑一個|挑一款|看一下/g, '幫我找']
    ];
    return replacements.reduce(
        (normalized, [pattern, replacement]) => normalized.replace(pattern, replacement),
        message
    );
}

// 先用可確定的條件更新上一輪狀態，避免把 295 筆家具全部交給 Gemini。
function derivePreliminaryFilters(previousFilters, message) {
    message = normalizeSearchTerms(message);
    if (/(重新開始|清除條件|全部不限|沒有條件)/.test(message)) {
        return normalizeFilters();
    }

    const next = { ...previousFilters };
    const category = findMentionedOption(message, furnitureOptions.categories, ['沙發', '椅', '桌', '床', '櫃', '燈', '架']);
    const color = findMentionedOption(message, furnitureOptions.colors, ['黑', '白', '灰', '紅', '藍', '綠', '黃', '棕', '木色']);
    const material = findMentionedOption(message, furnitureOptions.materials, ['實木', '木', '金屬', '布', '皮', '玻璃', '塑膠']);

    if (category) next.category = category;
    if (color) next.color = color;
    if (material) next.material = material;
    if (/顏色.{0,6}(不限|都可以|不拘)/.test(message)) next.color = '';
    if (/材質.{0,6}(不限|都可以|不拘)/.test(message)) next.material = '';
    if (/(類別|種類).{0,6}(不限|都可以|不拘)/.test(message)) next.category = '';
    if (/(預算|價格).{0,6}(不限|都可以|不拘)|不看價格/.test(message)) next.maxPrice = 0;

    const maxPrice = extractMaxPrice(message);
    if (maxPrice > 0) next.maxPrice = maxPrice;

    const areaMatch = message.match(/(\d+(?:\.\d+)?)\s*坪/);
    if (areaMatch) next.roomAreaPing = Number(areaMatch[1]);
    return normalizeFilters(next);
}

// 以中文字雙字詞計算簡易相關度，讓風格與描述相近的家具優先進入候選清單。
function createBigrams(text) {
    const normalized = cleanText(text).toLocaleLowerCase('zh-TW').replace(/[^\p{L}\p{N}]+/gu, '');
    const terms = new Set();
    for (let index = 0; index < normalized.length - 1; index += 1) {
        terms.add(normalized.slice(index, index + 2));
    }
    return terms;
}

function candidateScore(item, queryTerms) {
    const searchableText = [item.name, item.category, item.color, item.material, item.description]
        .map(cleanText)
        .join(' ')
        .toLocaleLowerCase('zh-TW');
    let score = 0;
    for (const term of queryTerms) {
        if (searchableText.includes(term)) score += 1;
    }
    return score;
}

function selectCandidates(catalog, filters, message, history) {
    const exactMatches = catalog.filter(item => matchesHardFilters(item, filters));
    // 條件過嚴而完全無結果時保留全目錄，讓 Gemini 能說明應放寬哪個條件。
    const source = exactMatches.length ? exactMatches : catalog;
    const recentUserText = history.filter(item => item.role === '使用者').map(item => item.text).join(' ');
    const queryTerms = createBigrams(`${recentUserText} ${message} ${filters.style}`);
    return source
        .map(item => ({ item, score: candidateScore(item, queryTerms) }))
        .sort((left, right) => right.score - left.score || Number(left.item.id) - Number(right.item.id))
        .slice(0, MAX_AI_CANDIDATES)
        .map(entry => entry.item);
}

// 先用快速規則判斷；回傳 null 代表語意不明確，需要交給 AI 做短分類。
function detectSearchModeByRules(message, previousFilters) {
    const normalizedMessage = normalizeSearchTerms(message);
    const shoppingWords = /(幫我找|找一個|找一下|推薦|挑選|選購|有沒有|我想要|我要|需要|預算|價格|以下|以內|便宜|貴一點|換成|改成|尺寸|幾坪|適合放|符合|購買|買一個)/;
    const constraintWords = /(沙發|椅|桌|床|櫃|燈|架|家具|黑|白|灰|紅|藍|綠|黃|棕|木色|實木|木製|金屬|布料|皮革|玻璃|塑膠)/;
    const followUpWords = /(便宜一點|貴一點|大一點|小一點|換一個|其他|還有嗎|再看看|不要這個|材質不限|顏色不限|價格不限)/;
    const chatWords = /^(你好|嗨|哈囉|謝謝|感謝|再見|你是誰|你可以做什麼)[！!。.]?$|怎麼保養|如何清潔|是什麼|為什麼/;
    const hasActiveFilters = Object.entries(previousFilters)
        .some(([key, value]) => key !== 'style' && (typeof value === 'number' ? value > 0 : Boolean(value)));

    if (shoppingWords.test(normalizedMessage)
        || (constraintWords.test(normalizedMessage) && /(想要|需要|找|買|推薦|適合)/.test(normalizedMessage))
        || (hasActiveFilters && followUpWords.test(normalizedMessage))) return true;
    if (chatWords.test(normalizedMessage)) return false;
    return null;
}

// 規則無法確定時才呼叫輕量模型分類，避免一般明確訊息產生額外等待。
async function shouldSearchFurniture(message, history, previousFilters) {
    const ruleResult = detectSearchModeByRules(message, previousFilters);
    if (ruleResult !== null) return ruleResult;

    const recentContext = history.slice(-4).map(item => `${item.role}：${item.text}`).join('\n');
    try {
        const result = await ai.models.generateContent({
            model: process.env.GEMINI_MODEL || 'gemini-3.5-flash-lite',
            contents: `最近對話：\n${recentContext || '無'}\n最新訊息：${message}`,
            config: {
                systemInstruction: '判斷使用者是否要搜尋、篩選或推薦資料庫中的家具。純聊天、問候、知識問答或保養問題不是搜尋。只回傳指定 JSON。',
                temperature: 0,
                thinkingConfig: { thinkingLevel: 'MINIMAL' },
                maxOutputTokens: 50,
                responseMimeType: 'application/json',
                responseSchema: {
                    type: Type.OBJECT,
                    properties: { search: { type: Type.BOOLEAN } },
                    required: ['search']
                }
            }
        });
        return JSON.parse(result.text.trim()).search === true;
    } catch (error) {
        console.warn('對話模式判斷失敗，本輪改用一般聊天:', error.message);
        return false;
    }
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
        const searchMode = await shouldSearchFurniture(message, history, previousFilters);
        // 搜尋模式才更新條件及挑選候選商品；一般聊天沿用條件，但不傳送家具清單。
        const preliminaryFilters = searchMode
            ? derivePreliminaryFilters(previousFilters, message)
            : previousFilters;
        const candidates = searchMode
            ? selectCandidates(catalog, preliminaryFilters, message, history)
            : [];
        console.log(searchMode
            ? `對話模式：家具搜尋；候選家具：${candidates.length}/${catalog.length} 筆`
            : '對話模式：一般聊天；本輪不搜尋家具');
        const historyText = history.length
            ? history.map(item => `${item.role}：${item.text}`).join('\n')
            : '無先前對話';
        const aiKnowledgeBase = candidates.map(f => (
            `ID: ${f.id} | 名稱: ${f.name} | 類別: ${f.category || ''}` +
            ` | 顏色: ${f.color || ''} | 材質: ${f.material || ''}` +
            ` | 價格: ${Number(f.price) || 0}元` +
            ` | 尺寸: ${f.width || 0}x${f.length_cm || 0}x${f.height || 0}cm` +
            ` | 描述: ${f.description || ''}`
        )).join('\n');

        const systemInstruction = `
你是親切、自然的室內設計與家具 AI 助理，必須延續最近的對話內容。
本輪模式：${searchMode ? '家具搜尋' : '一般聊天'}
資料庫允許的類別：${formatOptions(furnitureOptions.categories)}
資料庫允許的顏色：${formatOptions(furnitureOptions.colors)}
資料庫允許的材質：${formatOptions(furnitureOptions.materials)}
上一輪累積條件：${JSON.stringify(previousFilters)}
程式預先解析條件：${JSON.stringify(preliminaryFilters)}
最近對話：\n${historyText}
真實家具清單：\n${aiKnowledgeBase}

規則：
1. 家具搜尋模式才根據最新訊息更新 filters；沒有改動的條件必須沿用上一輪。
2. 使用者說不限、取消或都可以時，對應文字欄位回傳空字串，數字欄位回傳 0。
3. category、color、material 優先使用資料庫實際值；無法確認時用空字串。
4. category、color、material、maxPrice 是硬條件，推薦項目必須全部符合。
5. roomAreaPing、style 是軟偏好，用於尺寸與風格排序，不是絕對排除條件。
6. recommendations 只能包含真實整數 ID，最多 8 筆；沒有符合項目時回傳空陣列。
7. 家具搜尋模式的 reply 要說明沿用了哪些條件；若沒有符合項目，指出可以放寬的條件。
8. 一般聊天模式要像自然對話一樣回答，可以回應問候、空間規劃及家具知識；recommendations 必須是空陣列，而且 filters 必須原樣保留。
9. 若問題完全偏離室內設計、居家生活與家具，可以簡短回答後自然引導回你的專長。
10. 只回傳指定 JSON 結構。`;

        // 每個請求各自記錄開始時間，避免多人同時詢問時共用 console.time 標籤而互相衝突。
        const geminiStartedAt = Date.now();
        const aiResponse = await ai.models.generateContent({
            // Flash-Lite 適合高頻、低延遲的分類與結構化資料擷取。
            model: process.env.GEMINI_MODEL || 'gemini-3.5-flash-lite',
            contents: message,
            config: {
                systemInstruction,
                temperature: 0.2,
                // 使用低思考層級理解多輪條件，同時避免預設思考造成過長等待。
                thinkingConfig: { thinkingLevel: 'LOW' },
                maxOutputTokens: 500,
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
        console.log(`Gemini 回應: ${((Date.now() - geminiStartedAt) / 1000).toFixed(3)}s`);

        let aiResult;
        try {
            aiResult = JSON.parse(aiResponse.text.trim());
        } catch (error) {
            console.error('AI 回傳的不是合法 JSON:', aiResponse.text.trim());
            return res.status(502).json({ error: 'AI 回應格式異常，請再試一次。' });
        }

        // 一般聊天不得意外改動使用者累積的搜尋條件。
        const filters = searchMode ? normalizeFilters(aiResult.filters) : previousFilters;
        const catalogById = new Map(catalog.map(item => [Number(item.id), item]));
        const recommendations = [...new Set(
            (searchMode && Array.isArray(aiResult.recommendations) ? aiResult.recommendations : [])
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
            filters,
            mode: searchMode ? 'search' : 'chat'
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
