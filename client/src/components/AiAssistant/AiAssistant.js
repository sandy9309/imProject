// src/components/AiAssistant/AiAssistant.js
// 🤖 AI 空間設計小幫手:右下角圓形懸浮按鈕,點開展開聊天視窗
// 回傳格式(已與 AI 後端定案):{ reply: "文字", recommendations: [furniture_id, ...] }
import React, { useState, useRef, useEffect } from 'react';
import { MessageCircle, X, Send, Sparkles, Plus, Box } from 'lucide-react';
import { Link } from 'react-router-dom';
import { showToast, showConfirm } from '../Ui/ui';
import './AiAssistant.css';

// 🌐 AI 小幫手後端(埠號 5051,已與 AI 後端確認)
const AI_API_BASE = 'http://163.13.202.116:5051';
// 🌐 家具資料後端(用來把推薦的 id 轉成縮圖/名稱/價格)
const API_BASE = 'http://163.13.202.116:5050';

const MAX_QTY = 10;

// 跟型錄頁同一套模型網址處理:多欄位相容 + githack 跨域代理
const getModelUrl = (item) => {
  const rawUrl = item.download_url || item.model_url || item.glb_url || '';
  if (rawUrl.includes('raw.githubusercontent.com')) {
    return rawUrl.replace('raw.githubusercontent.com', 'raw.githack.com');
  }
  return rawUrl;
};

const AiAssistant = () => {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  const [sending, setSending] = useState(false);
  const [furnitureMap, setFurnitureMap] = useState({});
  const [recommendationSort, setRecommendationSort] = useState('recommended');
  // 🚀 3D 預覽中的家具(null = 沒開)
  const [preview, setPreview] = useState(null);
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

  // 第一次打開聊天視窗時,載入家具對照表(id → 縮圖/名稱/價格/3D模型)
  useEffect(() => {
    if (!open || Object.keys(furnitureMap).length > 0) return;
    const fetchFurnitures = async () => {
      try {
        const res = await fetch(`${API_BASE}/api/furnitures`);
        if (!res.ok) return;
        const data = await res.json();
        const list = Array.isArray(data) ? data : (data.data || []);
        const map = {};
        list.forEach(f => { map[f.id] = f; });
        setFurnitureMap(map);
      } catch (err) {
        console.error('AI 小幫手載入家具對照表失敗:', err);
      }
    };
    fetchFurnitures();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

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

      // 🚀 只保留「真實存在於型錄」的推薦 id(容錯:AI 萬一給了不存在的 id 就濾掉)
      const recs = (Array.isArray(data.recommendations) ? data.recommendations : [])
        .filter(id => furnitureMap[id]);

      setMessages(prev => [
        ...prev,
        { role: 'ai', text: data.reply || '(沒有收到回覆)', recs },
      ]);
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

  // ── 從對話直接加入配置清單(與型錄頁同一套 localStorage 邏輯)──
  const addToCart = async (furnitureId) => {
    const product = furnitureMap[furnitureId];
    if (!product) return;

    // 未登入 → 引導登入
    const isLoggedIn = !!localStorage.getItem('token') && !!localStorage.getItem('user_id');
    if (!isLoggedIn) {
      const goLogin = await showConfirm({
        title: '需要先登入',
        message: '登入後才能將家具加入配置清單,要前往登入嗎?',
        confirmText: '前往登入',
        cancelText: '再逛逛',
      });
      if (goLogin) window.location.href = '/login';
      return;
    }

    const currentCart = JSON.parse(localStorage.getItem('cart')) || [];
    const existingIndex = currentCart.findIndex(item =>
      (item.id === product.id) || (item.product_id === product.id)
    );

    if (existingIndex > -1) {
      const currentQty = currentCart[existingIndex].quantity || 1;
      if (currentQty >= MAX_QTY) {
        showToast(`「${product.name}」已達單款上限(${MAX_QTY} 個)`, 'error');
        return;
      }
      const updatedCart = [...currentCart];
      updatedCart[existingIndex] = { ...updatedCart[existingIndex], quantity: currentQty + 1 };
      localStorage.setItem('cart', JSON.stringify(updatedCart));
      showToast(`已增加為 ${currentQty + 1} 個「${product.name}」`, 'success');
    } else {
      const formattedProduct = {
        id: product.id,
        product_id: product.id,
        name: product.name,
        price: Number(product.price || 0),
        image: product.image_url || '',
        image_url: product.image_url || '',
        length_cm: product.length_cm,
        width: product.width,
        height: product.height,
        quantity: 1,
      };
      localStorage.setItem('cart', JSON.stringify([...currentCart, formattedProduct]));
      showToast(`🎉 ${product.name} 已加入配置清單!`, 'success');
    }
  };

  const onKeyDown = (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  };

  const sortRecommendations = (recommendations) => {
    if (recommendationSort === 'recommended') return recommendations;

    return [...recommendations].sort((aId, bId) => {
      const aPrice = Number(furnitureMap[aId]?.price) || 0;
      const bPrice = Number(furnitureMap[bId]?.price) || 0;

      return recommendationSort === 'price-asc'
        ? aPrice - bPrice
        : bPrice - aPrice;
    });
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
              <React.Fragment key={i}>
                <div className={`ai-msg ${m.role === 'user' ? 'ai-msg-user' : 'ai-msg-bot'}`}>
                  {m.text}
                </div>

                {/* 🚀 推薦家具卡片:點縮圖可開 3D 預覽 */}
                {m.recs && m.recs.length > 0 && (
                  <div className="ai-rec-list">
                    <div className="ai-rec-sort-row">
                      <label htmlFor={`ai-rec-sort-${i}`}>推薦排序</label>
                      <select
                        id={`ai-rec-sort-${i}`}
                        className="ai-rec-sort"
                        value={recommendationSort}
                        onChange={e => setRecommendationSort(e.target.value)}
                        aria-label="推薦家具排序方式"
                      >
                        <option value="recommended">AI 推薦順序</option>
                        <option value="price-asc">價格：低到高</option>
                        <option value="price-desc">價格：高到低</option>
                      </select>
                    </div>
                    {sortRecommendations(m.recs).map(id => {
                      const f = furnitureMap[id];
                      return (
                        <div key={id} className="ai-rec-card">
                          <button
                            className="ai-rec-thumb"
                            onClick={() => setPreview(f)}
                            title="點擊查看 3D 模型"
                            aria-label={`查看 ${f.name} 的 3D 模型`}
                          >
                            <img
                              src={f.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=200'}
                              alt={f.name}
                            />
                            <span className="ai-rec-3d-badge"><Box size={11} /> 3D</span>
                          </button>
                          <div className="ai-rec-info">
                            <span className="ai-rec-name">{f.name}</span>
                            <span className="ai-rec-price">NT$ {Number(f.price || 0).toLocaleString()}</span>
                          </div>
                          <button
                            className="ai-rec-add"
                            onClick={() => addToCart(id)}
                            aria-label={`將 ${f.name} 加入配置清單`}
                            title="加入配置清單"
                          >
                            <Plus size={16} />
                          </button>
                        </div>
                      );
                    })}
                    <Link className="ai-rec-more" to="/catalog" onClick={() => setOpen(false)}>
                      想看更多?前往家具型錄 →
                    </Link>
                  </div>
                )}
              </React.Fragment>
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

      {/* ── 3D 預覽彈窗(跟型錄頁同一套 model-viewer)── */}
      {preview && (
        <div className="ai-3d-overlay" onClick={() => setPreview(null)}>
          <div className="ai-3d-box" onClick={e => e.stopPropagation()}>
            <button className="ai-3d-close" onClick={() => setPreview(null)} aria-label="關閉">
              <X size={20} />
            </button>
            <h3 className="ai-3d-title">{preview.name} - 3D 預覽</h3>
            <div className="ai-3d-model">
              <model-viewer
                src={getModelUrl(preview)}
                camera-controls
                auto-rotate
                shadow-intensity="1"
                style={{ width: '100%', height: '100%' }}
              >
                <div slot="poster" className="ai-3d-poster">
                  ⏳ 3D 互動模型讀取中,請稍候...
                </div>
              </model-viewer>
            </div>
            <div className="ai-3d-footer">
              <span className="ai-3d-price">NT$ {Number(preview.price || 0).toLocaleString()}</span>
              <button
                className="ai-3d-add"
                onClick={() => addToCart(preview.id)}
              >
                加入配置清單
              </button>
            </div>
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
