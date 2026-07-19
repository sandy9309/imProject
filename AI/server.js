
const express = require('express');
const cors = require('cors');
const dotenv = require('dotenv');//讀取.env敏感金鑰
const { GoogleGenAI } = require('@google/genai');

dotenv.config();//環境變數初始化
const app = express();
const port = process.env.PORT || 5051;

app.use(cors());//允許其他埠號請求，跨網域存取
app.use(express.json());
app.use(express.static('public'));// 提供 public 資料夾靜態檔案服務，讓前端可以直接載入這裡的資源

const ai = new GoogleGenAI({ apiKey: process.env.GEMINI_API_KEY });

const axios=require('axios');


app.post('/api/chat', async (req, res) => {
    try {
        const { message } = req.body;

        if (!message) {
            return res.status(400).json({ error: '請輸入訊息' });
        }

        //  API 撈取所有真實家具資料(axios:發送請求/await:執行完才能繼續下一行)
        const catalogResponse = await axios.get('http://163.13.202.116:5050/api/furnitures');
        const dbfurnitureList = catalogResponse.data; // 家具陣列

        // 餵給 AI 的資料(map:把陣列每一筆資料拿來改成新的陣列)
        const aiKnowledgeBase = dbfurnitureList.map(f => {
            return `ID: ${f.id} | 名稱: ${f.name} | 類別: ${f.category} | 價格: $${f.price}元  | 尺寸: ${f.width}x${f.length_cm}x${f.height}cm | 描述: ${f.description || ''}`;
        }).join('\n');

        //設定系統提示詞
        const systemInstruction = `
                你是一個專業的室內設計助理。以下是目前資料庫擁有的真實家具清單：
                ${aiKnowledgeBase}

                請根據使用者的空間與風格需求，從上方清單中選擇適合的家具推薦給他。
                【嚴格回應規則】：
                1. 你「必須」且「只能」使用 JSON 格式回應，不要包含任何 markdown 標籤（如 \`\`\`json）。
                2. JSON 結構必須包含：
                - "reply": 純文字的推薦理由（文字裡不要提到任何家具名稱與價格，只要講推薦理由與搭配建議就好）。
                - "recommendations": 一個數字陣列，裡面只放你推薦的家具的真實 "ID"。如果沒有合適的，請給空陣列 []。

                範例回應格式：
                {
                "reply": "根據你的北歐風需求，我推薦了幾款簡約舒適的椅子，它們的尺寸非常適合 4 坪的空間。",
                "recommendations": [5, 12]
                }`;

        // 呼叫 Gemini AI
        const aiResponse = await ai.models.generateContent({
            model: 'gemini-2.5-flash',
            contents: message,
            config: {
                systemInstruction: systemInstruction,
                temperature: 0.3, // 降低創意度，讓 AI 緊扣資料庫內容
            }
        });

        //解析 Gemini 回傳文字 再轉 JSON 字串
        let aiResult;
        try {
            aiResult = JSON.parse(aiResponse.text.trim());
        } catch (e) {
            console.error("AI 回傳的不是合法 JSON:", aiResponse.text.trim());
            // 防呆機制：如果 AI 沒給 JSON，做一個預設結構
            aiResult = {
                reply: aiResponse.text.trim(),
                recommendations: []
            };
        }

        //  回傳前端
        res.json({
            reply: aiResult.reply,
            recommendations: aiResult.recommendations
        });

    } catch (error) {
        console.error('AI 或網路處理出錯:', error);
        res.status(500).json({ error: 'AI 伺服器發生錯誤。' });
    }
});


app.listen(port, () => {
    console.log(` AI 模擬伺服器已啟動：http://localhost:${port}`);
});