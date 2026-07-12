const express = require('express');
const cors = require('cors');
const dotenv = require('dotenv');
const { GoogleGenAI } = require('@google/genai');
const mysql = require('mysql2/promise'); // 引入資料庫套件

dotenv.config();

const app = express();
const port = process.env.PORT || 5051;

app.use(cors());
app.use(express.json());
app.use(express.static('public'));// 提供 public 資料夾靜態檔案服務，讓前端可以直接載入這裡的資源

const ai = new GoogleGenAI({ apiKey: process.env.GEMINI_API_KEY });

// 根據資料庫欄位，建立一組「模擬家具清單」
// 包含 id, name, category, width, length_cm, height, description
//  建立學校伺服器資料庫的連線池（Connection Pool）
const pool = mysql.createPool({
    host: '163.13.202.116',      // 學校伺服器 IP
    port: 3306,                  // 連線埠
    user: 'root',                // 帳號
    password: '06210621',        // 密碼
    database: 'ar_furniture_db', // 資料庫名稱
    waitForConnections: true,
    connectionLimit: 10,
    queueLimit: 0
});

app.post('/api/chat', async (req, res) => {
    try {
        const { message } = req.body;

        if (!message) {
            return res.status(400).json({ error: '請輸入訊息' });
        }

        // 1. 從資料庫撈取所有家具資料
        const [rows] = await pool.query('SELECT * FROM furnitures');
        
        // 2. 整理餵給 AI 的資料（加入價格 price，讓 AI 也可以根據預算推薦！）
        const furnitureSummary = rows.map(f => {
            const img = f.thumb_path || f.image_url || '';
            return `名稱: ${f.name} | 類別: ${f.category} | 價格: $${f.price}元 | 尺寸: ${f.width}x${f.length_cm}x${f.height}cm | 圖片: ${img} | 描述: ${f.description}`;
        }).join('\n');

        const response = await ai.models.generateContent({
            model: 'gemini-2.5-flash',
            contents: message,
            config: {
                //  更新系統指令：強制 AI 只能從這份清單中做推薦，並要求它回傳家具 ID
                systemInstruction: `你是一位專門輔助 MR 家具擺設的室內設計 AI 小幫手。
                這裡有我們目前系統支援的 3D 家具清單資料（包含尺寸與描述）：
                ${furnitureSummary}

                任務指南：
                1. 請根據使用者的風格喜好、空間大小，從上述清單中挑選「最適合」的家具推薦給他。
                2. 回答請親切、專業、條列式。
                3.項目之間請務必以「換行（Enter）」分隔，保持版面乾淨易讀，絕對不可以把多個家具寫在同一行。
                4.使用「•」作為標題！
                
                【回答格式範例】：
                您好！很高興為您推薦適合的家具。根據您的需求，我推薦以下款式：

                您好！根據您的需求與預算，為您推薦以下款式：<br><br>

                • <b>GLOSTAD 雙人沙發</b>（NT$ 5,420）<br>
                尺寸：124x79x77cm｜特色：現代簡約設計，適合小坪數。<br>
                <img src="https://example.com/glostad.jpg" style="max-width: 180px; border-radius: 8px; margin: 8px 0;" /><br><br>

                • <b>MICKE 書桌</b>（NT$ 2,250）<br>
                尺寸：73x50x75cm｜特色：簡潔附收納。<br>
                <img src="https://example.com/micke.jpg" style="max-width: 180px; border-radius: 8px; margin: 8px 0;" /><br><br>

                請問您有預算上的限制或偏好的顏色嗎？`,

                temperature: 0.3, // 降低創意度，讓 AI 更嚴謹地根據資料回答
            }
        });

        const aiReply = response.text;

        // 4. 直接把含有 HTML 圖片與換行的文字回傳給前端即可！
        res.json({ reply: response.text });

    } catch (error) {
        console.error('AI 或資料庫處理出錯:', error);
        res.status(500).json({ error: 'AI 伺服器發生錯誤。' });
    }
});

app.listen(port, () => {
    console.log(` AI 模擬伺服器已啟動：http://localhost:${port}`);
});