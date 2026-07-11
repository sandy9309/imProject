// src/pages/Catalog/Catalog.js
import React, { useState, useEffect } from 'react'; 
import { Search, Filter, Box, X, Maximize, PackagePlus } from 'lucide-react';
import './Catalog.css';
import { showToast, showConfirm } from '../../components/Ui/ui';

// 🌐 學校伺服器的正式內網 IP 網址
const API_BASE = 'http://163.13.202.116:5050';

const Catalog = () => {
  const [showFilters, setShowFilters] = useState(false);
  const [selectedItem, setSelectedItem] = useState(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [activeCategory, setActiveCategory] = useState("全部");
  // 🚀 尺寸區間：長/寬/高各有最小、最大，可擇一填寫、也可全填
  const [dims, setDims] = useState({
    minLength: '', maxLength: '',
    minWidth: '', maxWidth: '',
    minHeight: '', maxHeight: '',
  });
  // 🚀 金額範圍：可擇一填寫、也可全填
  const [priceRange, setPriceRange] = useState({ minPrice: '', maxPrice: '' });

  const [items, setItems] = useState([]);
  // 🚀 分類清單：從後端動態抓取，「全部」由前端固定保留在最前面
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  // 🚀 每次加入購物車後 +1，用來強制卡片重新渲染，即時反映最新數量
  const [cartVersion, setCartVersion] = useState(0);

  // 🚀 偵測是不是從「我的專案」點「新增家具」進來的，是的話顯示編輯中提示
  const editProjectId = localStorage.getItem('editProjectId');
  const editProjectName = localStorage.getItem('editProjectName');

  useEffect(() => {
    const fetchFurnitures = async () => {
      try {
        setLoading(true);
        setError(null);

        const response = await fetch(`${API_BASE}/api/furnitures`, {
          method: 'GET',
          headers: { 'Content-Type': 'application/json' },
        });

        if (!response.ok) {
          throw new Error(`伺服器回應錯誤，狀態碼：${response.status}`);
        }

        const data = await response.json();
        console.log("後端成功撈到的原始家具資料：", data);
        
        setItems(data || []);
      } catch (err) {
        console.error("家具 API 連線失敗:", err);
        setError(err.message);
      } finally {
        setLoading(false);
      }
    };

    // 🚀 動態抓取分類清單（床、椅子、書桌、沙發、收納、桌子...），前端不寫死
    const fetchCategories = async () => {
      try {
        const response = await fetch(`${API_BASE}/api/furnitures/categories`, {
          method: 'GET',
          headers: { 'Content-Type': 'application/json' },
        });
        if (!response.ok) throw new Error(`狀態碼：${response.status}`);
        const data = await response.json();
        // 容錯：後端可能回傳 ["床","椅子",...] 或 { data: [...] } 兩種包裝
        const rawList = Array.isArray(data) ? data : (data.data || []);
        // 🚀 正規化：每一項可能是純字串 "床"，也可能是物件 { name: "床" } 或 { category: "床" }
        // 統一轉成純文字，避免 React 渲染物件時報錯
        const list = rawList
          .map(c => (typeof c === 'string' ? c : (c?.name ?? c?.category ?? '')))
          .filter(Boolean);
        setCategories(list);
      } catch (err) {
        console.error("分類清單 API 連線失敗:", err);
        // 抓不到就先空著，畫面只顯示「全部」，不影響其他功能
        setCategories([]);
      }
    };

    fetchFurnitures();
    fetchCategories();
  }, []);

  // 🚀 通用的區間比對小工具：沒填的欄位不列入條件
  const inRange = (value, min, max) => {
    if (min !== '' && value < Number(min)) return false;
    if (max !== '' && value > Number(max)) return false;
    return true;
  };

  const filteredItems = items.filter(item => {
    const name = item.name || '';
    const category = item.category || '其它';
    
    // 💡 修正：精準對齊後端回傳的真實欄位
    const itemL = Number(item.length_cm || 0);
    const itemW = Number(item.width || 0);
    const itemH = Number(item.height || 0);
    const itemPrice = Number(item.price || 0);

    // 1. 名稱搜尋邏輯
    const matchesSearch = name.toLowerCase().includes(searchTerm.toLowerCase());
    
    // 2. 分類篩選邏輯
    const matchesCategory = activeCategory === "全部" || category === activeCategory;
    
    // 3. 尺寸區間邏輯（最小 / 最大都可擇一填寫）
    const matchesL = inRange(itemL, dims.minLength, dims.maxLength);
    const matchesW = inRange(itemW, dims.minWidth, dims.maxWidth);
    const matchesH = inRange(itemH, dims.minHeight, dims.maxHeight);

    // 4. 金額範圍邏輯
    const matchesPrice = inRange(itemPrice, priceRange.minPrice, priceRange.maxPrice);

    return matchesSearch && matchesCategory && matchesL && matchesW && matchesH && matchesPrice;
  });

  const handleDimChange = (e) => {
    const v = e.target.value;
    // 🚀 禁止負數:清空可以,但只要有值就不能小於 0
    if (v !== '' && Number(v) < 0) return;
    setDims({ ...dims, [e.target.name]: v });
  };

  const handlePriceChange = (e) => {
    const v = e.target.value;
    if (v !== '' && Number(v) < 0) return;
    setPriceRange({ ...priceRange, [e.target.name]: v });
  };

  const resetFilters = () => {
    setDims({ minLength: '', maxLength: '', minWidth: '', maxWidth: '', minHeight: '', maxHeight: '' });
    setPriceRange({ minPrice: '', maxPrice: '' });
  };

  // 🛒 翻新後的 addToCart：支援同一家具重複加入(上限10個)，第二次以上會先跟使用者確認
  const MAX_QTY = 10;
  const addToCart = async (product) => {
    const currentCart = JSON.parse(localStorage.getItem('cart')) || [];

    const existingIndex = currentCart.findIndex(item =>
      (item.id === product.id) || (item.product_id === product.id)
    );

    if (existingIndex > -1) {
      const existingItem = currentCart[existingIndex];
      const currentQty = existingItem.quantity || 1;

      if (currentQty >= MAX_QTY) {
        showToast(`「${product.name}」已達單款上限（${MAX_QTY} 個），無法再加入囉！`, 'error');
        return;
      }

      const confirmed = await showConfirm({ message: `目前已加入 ${currentQty} 個「${product.name}」，是否要繼續增加？` });
      if (!confirmed) return;

      const updatedCart = [...currentCart];
      updatedCart[existingIndex] = { ...existingItem, quantity: currentQty + 1 };
      localStorage.setItem('cart', JSON.stringify(updatedCart));
      setCartVersion(v => v + 1);
    } else {
      // 🔥 核心關鍵：轉換格式，讓暫存結構跟後端回傳的欄位完全一致
      const formattedProduct = {
        id: product.id,                        // 這是家具本身的 ID
        product_id: product.id,                // 備份一組 product_id 供後端 POST 使用
        name: product.name,
        price: Number(product.price || 0),
        image: product.image_url || '',         // 對齊 Cart.js 渲染所需的 image 欄位
        image_url: product.image_url || '',
        length_cm: product.length_cm,          // 長度對齊後端 length_cm
        width: product.width,
        height: product.height,
        quantity: 1,                            // 🚀 新增：這件家具目前的加入件數
      };

      const updatedCart = [...currentCart, formattedProduct];
      localStorage.setItem('cart', JSON.stringify(updatedCart));
      setCartVersion(v => v + 1);
      showToast(`🎉 ${product.name} 已成功加入配置清單！`, 'success');
    }
  };

  const cancelEditMode = () => {
    localStorage.removeItem('editProjectId');
    localStorage.removeItem('editProjectName');
    window.location.href = '/projects';
  };

  return (
    <div className="catalog-container">
      {/* 🚀 編輯既有專案中的提示橫幅 */}
      {editProjectId && (
        <div className="catalog-edit-banner">
          <span>
            <PackagePlus size={18} style={{ verticalAlign: 'middle', marginRight: '6px' }} />
            正在為專案「{editProjectName || editProjectId}」挑選新家具，選好後請到「配置清單」按送出
          </span>
          <button onClick={cancelEditMode}>取消，返回專案</button>
        </div>
      )}

      {/* 頂部搜尋與篩選列 */}
      <div className="catalog-header">
        <h1>家具型錄</h1>
        <div className="search-bar">
          <div className="search-input">
            <Search size={18} />
            <input 
              type="text" 
              placeholder="搜尋家具名稱..." 
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)} 
            />
          </div>
          <button 
            className={`filter-btn ${showFilters ? 'active' : ''}`}
            onClick={() => setShowFilters(!showFilters)}
          >
            <Filter size={18} /> 篩選
          </button>
        </div>
      </div>
          
      {showFilters && (
        <div className="dimension-filter-dropdown">
          <div className="filter-title">
            <Maximize size={18} /> 尺寸區間 (公分)：
          </div>
          <div className="dim-inputs range-inputs">
            <div className="input-field">
              <label>長度</label>
              <div className="range-pair">
                <input name="minLength" type="number" min="0" placeholder="最小" value={dims.minLength} onChange={handleDimChange} />
                <span className="range-sep">~</span>
                <input name="maxLength" type="number" min="0" placeholder="最大" value={dims.maxLength} onChange={handleDimChange} />
              </div>
            </div>
            <div className="input-field">
              <label>寬度</label>
              <div className="range-pair">
                <input name="minWidth" type="number" min="0" placeholder="最小" value={dims.minWidth} onChange={handleDimChange} />
                <span className="range-sep">~</span>
                <input name="maxWidth" type="number" min="0" placeholder="最大" value={dims.maxWidth} onChange={handleDimChange} />
              </div>
            </div>
            <div className="input-field">
              <label>高度</label>
              <div className="range-pair">
                <input name="minHeight" type="number" min="0" placeholder="最小" value={dims.minHeight} onChange={handleDimChange} />
                <span className="range-sep">~</span>
                <input name="maxHeight" type="number" min="0" placeholder="最大" value={dims.maxHeight} onChange={handleDimChange} />
              </div>
            </div>
          </div>

          <div className="filter-title" style={{ marginTop: '16px' }}>
            💰 金額範圍 (NT$)：
          </div>
          <div className="dim-inputs range-inputs">
            <div className="input-field">
              <label>價格</label>
              <div className="range-pair">
                <input name="minPrice" type="number" min="0" placeholder="最低" value={priceRange.minPrice} onChange={handlePriceChange} />
                <span className="range-sep">~</span>
                <input name="maxPrice" type="number" min="0" placeholder="最高" value={priceRange.maxPrice} onChange={handlePriceChange} />
              </div>
            </div>
            <button className="reset-btn" onClick={resetFilters}>清除全部條件</button>
          </div>
        </div>
      )}

      {/* 分類切換按鈕：「全部」固定在最前，其餘從後端動態抓取 */}
      <div className="category-filter">
        {["全部", ...categories].map(cat => (
          <button 
            key={cat}
            className={`filter-tag ${activeCategory === cat ? 'active' : ''}`}
            onClick={() => setActiveCategory(cat)}
          >
            {cat}
          </button>
        ))}
      </div>

      {/* 狀態分流機制 */}
      {loading ? (
        <div className="no-results">
          <div className="loading-wrap"><span className="loading-spinner" />家具型錄載入中...</div>
        </div>
      ) : error ? (
        <div className="no-results" style={{ color: '#ef4444' }}>
          <p>❌ 無法連線至後端伺服器：{error}</p>
          <p style={{ fontSize: '14px', color: '#64748b', marginTop: '8px' }}>請確認後端同學的 Ngrok 是否正常開啟</p>
        </div>
      ) : (
        /* 家具展示網格 */
        <div className="catalog-grid" key={`grid-${cartVersion}`}>
          {filteredItems.map(item => {
            return (
            <div key={item.id} className="furniture-card">
              <div className="image-wrapper">
                <img src={item.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'} alt={item.name} />
                <div className="category-tag">{item.category || '其它'}</div>
                <div className="dim-tag">
                  {item.length_cm}x{item.width}x{item.height} cm
                </div>
              </div>
              <div className="card-info">
                <h3>{item.name}</h3>
                <p className="price">NT$ {Number(item.price || 0).toLocaleString()}</p>
                <div className="card-buttons">
                  <button className="preview-btn" onClick={() => setSelectedItem(item)}>
                    <Box size={16} /> 3D 模擬預覽
                  </button>
                </div>
              </div>
            </div>
            );
          })}
        </div>
      )}

      {/* 找不到結果時的顯示 */}
      {!loading && !error && filteredItems.length === 0 && (
        <div className="no-results">
          <p>找不到符合條件的家具喔！</p>
        </div>
      )}

      {/* 3D 彈窗邏輯 */}
      {selectedItem && (
        <div className="modal-overlay" onClick={() => setSelectedItem(null)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <button className="close-btn" onClick={() => setSelectedItem(null)}>
              <X size={24} />
            </button>
            
            <h2>{selectedItem.name} - 3D 預覽</h2>
            
            <div className="model-container">
              <model-viewer 
                src={
                  (() => {
                    // 1. 多重相容撈出網址結構
                    const rawUrl = selectedItem.download_url || selectedItem.model_url || selectedItem.glb_url || '';
                    
                    // 2. 核心跨域破解：自動將 GitHub Raw 網址替換為 githack 代理
                    if (rawUrl.includes('raw.githubusercontent.com')) {
                      return rawUrl.replace('raw.githubusercontent.com', 'raw.githack.com');
                    }
                    return rawUrl;
                  })()
                } 
                camera-controls 
                auto-rotate 
                shadow-intensity="1"
              >
                {/* ⏳ 讀取提示層：完全交給 CSS 去做定位與圓角控制 */}
                <div slot="poster" className="model-loading-poster">
                  ⏳ 3D 互動模型讀取中，請稍候...
                </div>
              </model-viewer>
            </div>
            
            <div className="modal-footer">
              <p className="modal-price">商品價格：NT$ {Number(selectedItem.price || 0).toLocaleString()}</p>
              <button className="action-btn" onClick={() => addToCart(selectedItem)}>加入配置清單</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Catalog;