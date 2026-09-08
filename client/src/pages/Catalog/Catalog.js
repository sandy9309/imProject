// src/pages/Catalog/Catalog.js
import React, { useState, useEffect } from 'react'; 
import { Search, Filter, Box, X, Maximize, PackagePlus, ArrowUpDown, ArrowUp, ArrowDown, Ruler } from 'lucide-react';
import './Catalog.css';
import { showToast, showConfirm } from '../../components/Ui/ui';

const API_BASE = 'http://163.13.202.116:5050';

const Catalog = () => {
  const [showFilters, setShowFilters] = useState(false);
  const [selectedItem, setSelectedItem] = useState(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [activeCategory, setActiveCategory] = useState("全部");
  const [dims, setDims] = useState({
    minLength: '', maxLength: '',
    minWidth: '', maxWidth: '',
    minHeight: '', maxHeight: '',
  });
  const [priceRange, setPriceRange] = useState({ minPrice: '', maxPrice: '' });
  const [priceSort, setPriceSort] = useState('none');
  const [items, setItems] = useState([]);
  const [categories, setCategories] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [cartVersion, setCartVersion] = useState(0);
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
        setItems(data || []);
      } catch (err) {
        console.error("家具 API 連線失敗:", err);
        setError(err.message);
      } finally {
        setLoading(false);
      }
    };
    const fetchCategories = async () => {
      try {
        const response = await fetch(`${API_BASE}/api/furnitures/categories`, {
          method: 'GET',
          headers: { 'Content-Type': 'application/json' },
        });
        if (!response.ok) throw new Error(`狀態碼：${response.status}`);
        const data = await response.json();
        const rawList = Array.isArray(data) ? data : (data.data || []);
        const list = rawList
          .map(c => (typeof c === 'string' ? c : (c?.name ?? c?.category ?? '')))
          .filter(Boolean);
        setCategories(list);
      } catch (err) {
        console.error("分類清單 API 連線失敗:", err);
        setCategories([]);
      }
    };

    fetchFurnitures();
    fetchCategories();
  }, []);

  const inRange = (value, min, max) => {
    if (min !== '' && value < Number(min)) return false;
    if (max !== '' && value > Number(max)) return false;
    return true;
  };

  const filteredItems = items.filter(item => {
    const name = item.name || '';
    const category = item.category || '其它';
    const itemL = Number(item.length_cm || 0);
    const itemW = Number(item.width || 0);
    const itemH = Number(item.height || 0);
    const itemPrice = Number(item.price || 0);
    const matchesSearch = name.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesCategory = activeCategory === "全部" || category === activeCategory;
    const matchesL = inRange(itemL, dims.minLength, dims.maxLength);
    const matchesW = inRange(itemW, dims.minWidth, dims.maxWidth);
    const matchesH = inRange(itemH, dims.minHeight, dims.maxHeight);
    const matchesPrice = inRange(itemPrice, priceRange.minPrice, priceRange.maxPrice);

    return matchesSearch && matchesCategory && matchesL && matchesW && matchesH && matchesPrice;
  });

  const displayedItems = priceSort === 'none'
    ? filteredItems
    : [...filteredItems].sort((a, b) => {
        const pa = Number(a.price || 0);
        const pb = Number(b.price || 0);
        return priceSort === 'asc' ? pa - pb : pb - pa;
      });

  const togglePriceSort = () => {
    setPriceSort(prev =>
      prev === 'none' ? 'asc' : prev === 'asc' ? 'desc' : 'none'
    );
  };

  const sortLabel =
    priceSort === 'asc' ? '價格：低到高'
    : priceSort === 'desc' ? '價格：高到低'
    : '價格排序';

  const handleDimChange = (e) => {
    const v = e.target.value;
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

  const MAX_QTY = 10;
  const addToCart = async (product) => {
    const isLoggedIn = !!localStorage.getItem('token') && !!localStorage.getItem('user_id');
    if (!isLoggedIn) {
      const goLogin = await showConfirm({
        title: '需要先登入',
        message: '登入後才能將家具加入配置清單，要前往登入嗎？',
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
      {/* 編輯既有專案提示 */}
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

          {/* 價格排序按鈕，循環切換 低到高 → 高到低 → 預設 */}
          <button
            className={`filter-btn ${priceSort !== 'none' ? 'active' : ''}`}
            onClick={togglePriceSort}
            title="點擊切換價格排序"
          >
            {priceSort === 'asc' ? <ArrowUp size={18} />
              : priceSort === 'desc' ? <ArrowDown size={18} />
              : <ArrowUpDown size={18} />}
            {sortLabel}
          </button>

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
            價格範圍 (NT$)：
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

      {loading ? (
        <div className="no-results">
          <div className="loading-wrap"><span className="loading-spinner" />家具型錄載入中...</div>
        </div>
      ) : error ? (
        <div className="no-results">
          <p style={{ color: 'var(--color-danger)' }}>⚠️ 目前無法載入家具型錄</p>
          <p style={{ fontSize: '14px', color: 'var(--color-text-muted)', marginTop: '8px' }}>
            可能是網路連線問題，請稍後再試一次，或確認伺服器是否正常運作。
          </p>
        </div>
      ) : (
        <div className="catalog-grid" key={`grid-${cartVersion}`}>
          {displayedItems.map(item => {
            return (
            <div key={item.id} className="furniture-card">
              <div className="image-wrapper">
                <img src={item.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'} alt={item.name} />
                <div className="category-tag">{item.category || '其它'}</div>
              </div>
              <div className="card-info">
                <h3>{item.name}</h3>
                <p className="card-dimensions">
                  <Ruler size={14} />
                  {item.length_cm} × {item.width} × {item.height} cm
                </p>
                <p className="price">NT$ {Number(item.price || 0).toLocaleString()}</p>
                <div className="card-buttons">
                  <button className="preview-btn" onClick={() => setSelectedItem(item)}>
                    <Box size={16} /> 3D 預覽
                  </button>
                  <button className="card-add-btn" onClick={() => addToCart(item)} title="加入配置清單">
                    <PackagePlus size={16} /> 加入清單
                  </button>
                </div>
              </div>
            </div>
            );
          })}
        </div>
      )}

      {!loading && !error && displayedItems.length === 0 && (
        <div className="no-results">
          <p>找不到符合條件的家具喔！</p>
        </div>
      )}

      {/* 3D 彈窗 */}
      {selectedItem && (
        <div className="modal-overlay" onClick={() => setSelectedItem(null)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <button className="close-btn" onClick={() => setSelectedItem(null)}>
              <X size={24} />
            </button>
            
            <h2>{selectedItem.name} - 3D 預覽</h2>
            <p className="modal-dimensions">
              <Ruler size={15} />
              尺寸：{selectedItem.length_cm || '-'} × {selectedItem.width || '-'} × {selectedItem.height || '-'} cm
            </p>
            
            <div className="model-container">
              <model-viewer 
                src={
                  (() => {
                    const rawUrl = selectedItem.download_url || selectedItem.model_url || selectedItem.glb_url || '';
                    
                    // 將 GitHub Raw 網址替換為 githack 代理
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