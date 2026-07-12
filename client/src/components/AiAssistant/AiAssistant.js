// src/components/AiAssistant/AiAssistant.js
// 🤖 AI 空間設計小幫手:右下角圓形懸浮按鈕,點開展開聊天視窗
import React, { useState, useRef, useEffect } from 'react';
import { MessageCircle, X, Send, Sparkles } from 'lucide-react';
import './AiAssistant.css';

// 🌐 AI 小幫手後端網址(朋友的 Express 伺服器跑在哪就填哪)
// 本機開發時他的伺服器預設是 3000 埠,跟 React 衝突的話請他改埠號(例如 5051)
const AI_API_BASE = 'http://163.13.202.116:5051';

const AiAssistant = () => {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  const [sending, setSending] = useState(false);
  const [messages, setMessages] = useState([
    {
      role: 'ai',
      text: '你好!我是空間設計小幫手 🛋️ 告訴我你的空間大小或喜歡的風格,我幫你推薦適合的家具!',
    },
  ]);

  const bottomRef = useRef(null);

  // 每次有新訊息,自動捲到最底
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, open]);

  const sendMessage = async () => {
    const text = input.trim();
    if (!text || sending) return;

    setMessages(prev => [...prev, { role: 'user', text }]);
    setInput('');
    setSending(true);

    try {
      const res = await fetch(`${AI_API_BASE}/api/chat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: text }),
      });
      if (!res.ok) throw new Error(`伺服器回應 ${res.status}`);
      const data = await res.json();
      setMessages(prev => [...prev, { role: 'ai', text: data.reply || '(沒有收到回覆)' }]);
    } catch (err) {
      console.error('AI 小幫手連線失敗:', err);
      setMessages(prev => [
        ...prev,
        { role: 'ai', text: '⚠️ 連線失敗,請確認 AI 伺服器有開啟,稍後再試一次。' },
      ]);
    } finally {
      setSending(false);
    }
  };

  const onKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  };

  return (
    <>
      {/* ── 聊天視窗 ── */}
      {open && (
        <div className="ai-chat-window">
          <div className="ai-chat-header">
            <span className="ai-chat-title">
              <Sparkles size={16} /> AI 空間設計小幫手
            </span>
            <button className="ai-chat-close" onClick={() => setOpen(false)} aria-label="關閉">
              <X size={18} />
            </button>
          </div>

          <div className="ai-chat-messages">
            {messages.map((m, i) => (
              <div key={i} className={`ai-msg ${m.role === 'user' ? 'ai-msg-user' : 'ai-msg-bot'}`}>
                {m.text}
              </div>
            ))}
            {sending && (
              <div className="ai-msg ai-msg-bot ai-msg-typing">
                <span /><span /><span />
              </div>
            )}
            <div ref={bottomRef} />
          </div>

          <div className="ai-chat-input-row">
            <textarea
              className="ai-chat-input"
              rows={1}
              placeholder="描述你的空間或風格..."
              value={input}
              onChange={e => setInput(e.target.value)}
              onKeyDown={onKeyDown}
            />
            <button
              className="ai-chat-send"
              onClick={sendMessage}
              disabled={sending || !input.trim()}
              aria-label="送出"
            >
              <Send size={16} />
            </button>
          </div>
        </div>
      )}

      {/* ── 圓形懸浮按鈕 ── */}
      <button
        className={`ai-fab ${open ? 'open' : ''}`}
        onClick={() => setOpen(v => !v)}
        aria-label={open ? '關閉 AI 小幫手' : '開啟 AI 小幫手'}
      >
        {open ? <X size={24} /> : <MessageCircle size={24} />}
      </button>
    </>
  );
};

export default AiAssistant;