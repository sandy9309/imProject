-- 忘記密碼：一次性重設 token 表
-- 在 MySQL Workbench 對 ar_furniture_db 執行一次即可
CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    token VARCHAR(64) NOT NULL UNIQUE,
    expires_at DATETIME NOT NULL,          -- 到期時間（產生後 30 分鐘）
    used TINYINT(1) NOT NULL DEFAULT 0,    -- 是否已使用（一次性）
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_token (token),
    CONSTRAINT fk_prt_user FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
);
