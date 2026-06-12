// src/pages/Catalog/Catalog.js
import React, { useState, useEffect } from 'react'; 
import { Search, Filter, Box, X, Maximize } from 'lucide-react';
import './Catalog.css';

const Catalog = () => {
  const [showFilters, setShowFilters] = useState(false);
  const [selectedItem, setSelectedItem] = useState(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [activeCategory, setActiveCategory] = useState("全部");
  const [dims, setDims] = useState({ length: '', width: '', height: '' });

  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    const fetchFurnitures = async () => {
      try {
        setLoading(true);
        setError(null);

        // 🚀 加上破防機制的 fetch，穿透 Ngrok 免費版警告頁
        const response = await fetch('https://refulgently-unavailing-mathilda.ngrok-free.dev/api/furnitures', {
          method: 'GET',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('token') || ''}`,
            // 🔥 【核心關鍵】加入這行 Ngrok 專用破防標頭
            'ngrok-skip-browser-warning': 'true'
          }
        });

        if (!response.ok) {
          throw new Error(`伺服器回應錯誤，狀態碼：${response.status}`);
        }

        const data = await response.json();
        console.log("從 Ngrok 後端成功撈到的原始家具資料：", data);
        
        setItems(data || []);
      } catch (err) {
        console.error("家具 API 連線失敗:", err);
        setError(err.message);
      } finally {
        setLoading(false);
      }
    };

    fetchFurnitures();
  }, []);

  const filteredItems = items.filter(item => {
    const name = item.name || '';
    const category = item.category || '其它';
    
    // 💡 修正：精準對齊後端回傳的真實欄位
    const itemL = Number(item.length_cm || 0);
    const itemW = Number(item.width || 0);
    const itemH = Number(item.height || 0);

    // 1. 名稱搜尋邏輯
    const matchesSearch = name.toLowerCase().includes(searchTerm.toLowerCase());
    
    // 2. 分類篩選邏輯
    const matchesCategory = activeCategory === "全部" || category === activeCategory;
    
    // 3. 空間尺寸限制邏輯
    const matchesL = !dims.length || itemL <= Number(dims.length);
    const matchesW = !dims.width || itemW <= Number(dims.width);
    const matchesH = !dims.height || itemH <= Number(dims.height);

    return matchesSearch && matchesCategory && matchesL && matchesW && matchesH;
  });

  const handleDimChange = (e) => {
    setDims({ ...dims, [e.target.name]: e.target.value });
  };

  // 🛒 翻新後的 addToCart：建立與後端完全相容的格式
  const addToCart = (product) => {
    const currentCart = JSON.parse(localStorage.getItem('cart')) || [];
    
    // 💡 雙重檢查：同時比對 id 或 product_id，避免重複加入
    const isExist = currentCart.find(item => 
      (item.id === product.id) || 
      (item.product_id === product.id)
    );
    
    if (isExist) {
      alert("此家具已在您的配置清單中囉！");
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
        height: product.height
      };

      const updatedCart = [...currentCart, formattedProduct];
      localStorage.setItem('cart', JSON.stringify(updatedCart));
      alert(`🎉 ${product.name} 已成功加入配置清單！`);
    }
  };

  return (
    <div className="catalog-container">
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
            <Maximize size={18} /> 空間尺寸限制 (cm)：
          </div>
          <div className="dim-inputs">
            <div className="input-field">
              <label>最大長度</label>
              <input name="length" type="number" placeholder="cm" value={dims.length} onChange={handleDimChange} />
            </div>
            <div className="input-field">
              <label>最大寬度</label>
              <input name="width" type="number" placeholder="cm" value={dims.width} onChange={handleDimChange} />
            </div>
            <div className="input-field">
              <label>最大高度</label>
              <input name="height" type="number" placeholder="cm" value={dims.height} onChange={handleDimChange} />
            </div>
            <button className="reset-btn" onClick={() => setDims({ length: '', width: '', height: '' })}>清除限制</button>
          </div>
        </div>
      )}

      {/* 分類切換按鈕 */}
      <div className="category-filter">
        {["全部", "客廳", "餐廳", "書房", "臥室"].map(cat => (
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
          <p>⏳ 家具型錄載入中，請稍候...</p>
        </div>
      ) : error ? (
        <div className="no-results" style={{ color: '#ef4444' }}>
          <p>❌ 無法連線至後端伺服器：{error}</p>
          <p style={{ fontSize: '14px', color: '#64748b', marginTop: '8px' }}>請確認後端同學的 Ngrok 是否正常開啟</p>
        </div>
      ) : (
        /* 家具展示網格 */
        <div className="catalog-grid">
          {filteredItems.map(item => (
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
          ))}
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