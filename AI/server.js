const express = require('express');
const cors = require('cors');
const dotenv = require('dotenv');
const { GoogleGenAI } = require('@google/genai');
const mysql = require('mysql2/promise'); // 引入資料庫套件

dotenv.config();

const app = express();
const port = process.env.PORT || 3000;

app.use(cors());
app.use(express.json());
app.use(express.static('public'));// 提供 public 資料夾靜態檔案服務，讓前端可以直接載入這裡的資源

const ai = new GoogleGenAI({ apiKey: process.env.GEMINI_API_KEY });

// 🛒 根據你們的資料庫欄位，建立一組「模擬家具清單」
// 包含 id, name, category, width, length_cm, height, description
// 🎯 建立學校伺服器資料庫的連線池（Connection Pool）
const pool = mysql.createPool({
    host: '163.13.202.116',      // 學校伺服器 IP
    port: 3306,                  // 連線埠
    user: 'root',                // 帳號
    password: '06210621',        // 密碼
    database: 'ar_furniture_db', // 資料庫名稱
    waitForConnections: true,
    connectionLimit: 10,
    queueLimit: 0
});;

app.post('/api/chat', async (req, res) => {
    try {
        const { message } = req.body;

        if (!message) {
            return res.status(400).json({ error: '請輸入訊息' });
        }

        const [rows] = await pool.query('SELECT * FROM furnitures');

        // 將學校伺服器撈出來的真實家具轉成文字，餵給 AI 當作它的「知識庫」
        const furnitureContext = JSON.stringify(rows, null, 2);

        const response = await ai.models.generateContent({
            model: 'gemini-2.5-flash',
            contents: message,
            config: {
                // 📝 更新系統指令：強制 AI 只能從這份清單中做推薦，並要求它回傳家具 ID
                systemInstruction: `你是一位專門輔助 MR 家具擺設的室內設計 AI 小幫手。
                這裡有我們目前系統支援的 3D 家具清單資料（包含尺寸與描述）：
                ${furnitureContext}

                任務指南：
                1. 請根據使用者的風格喜好、空間大小，從上述清單中挑選「最適合」的家具推薦給他。
                2. 回答請親切、專業、條列式，控制在 150 字內。
                3. ✨超級重要：當你推薦某個家具時，請務必在回答中以 [FurnitureID: 數字] 的格式標註該家具的 id。例如：推薦您擺放 [FurnitureID: 1]。這將用來觸發我們的 MR 系統載入模型。`,

                temperature: 0.5, // 降低創意度，讓 AI 更嚴謹地根據資料回答
            }
        });

        res.json({ reply: response.text });

    } catch (error) {
        console.error('AI 或資料庫處理出錯:', error);
        res.status(500).json({ error: 'AI 伺服器發生錯誤。' });
    }
});

app.listen(port, () => {
    console.log(` AI 模擬伺服器已啟動：http://localhost:${port}`);
});