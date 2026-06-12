import React, { useState, useEffect } from 'react';
import { Trash2, ChevronRight, ShoppingBag } from 'lucide-react';
import './Cart.css';

const Cart = () => {
  const [isSaved, setIsSaved] = useState(false);
  const [loading, setLoading] = useState(false);
  const [cartItems, setCartItems] = useState([]);

  // 🚀 安全動態讀取：只讀取 Login 存進來的 user_id，沒登入就完全不給過
  const currentUserId = localStorage.getItem('user_id');

  useEffect(() => {
    // 防禦：如果根本沒有 user_id，代表沒登入，不發送 API
    if (!currentUserId) {
      console.warn("未偵測到登入的 User ID，請先登入！");
      return;
    }

    const fetchUserCart = async () => {
      try {
        setLoading(true);
        
        // 🚀 帳號切換防禦：如果本次讀取的帳號跟上次快取的帳號不同，直接洗掉暫存
        const lastCartUserId = localStorage.getItem('cart_user_id');
        if (lastCartUserId && lastCartUserId !== currentUserId) {
          localStorage.removeItem('cart');
        }
        localStorage.setItem('cart_user_id', currentUserId);

        // 🚀 動態發送 GET 請求給後端
        const response = await fetch(`https://refulgently-unavailing-mathilda.ngrok-free.dev/api/cart?userId=${currentUserId}`, {
          method: 'GET',
          headers: {
            'Authorization': `Bearer ${localStorage.getItem('token') || ''}`,
            'ngrok-skip-browser-warning': 'true' 
          }
        });

        if (response.ok) {
          const resBody = await response.json();
          const realCartList = resBody.data || [];
          
          if (realCartList.length > 0) {
            // 格式化欄位，確保資料庫流水號 id 與商品 product_id 區分開來
            const formattedList = realCartList.map(item => ({
              ...item,
              id: item.id, 
              product_id: item.product_id || item.productId || item.id 
            }));

            setCartItems(formattedList);
            localStorage.setItem('cart', JSON.stringify(formattedList));
            setIsSaved(true); 
            return;
          }
        }
      } catch (err) {
        console.error("後端連線失敗，採用本地快取備案:", err);
      } finally {
        setLoading(false);
      }

      const savedCart = JSON.parse(localStorage.getItem('cart')) || [];
      setCartItems(savedCart);
      setIsSaved(savedCart.length > 0);
    };

    fetchUserCart();
  }, [currentUserId]); 

  // 🗑️ 刪除單筆項目
  const removeItem = async (cartItemId) => {
    if (!cartItemId) return;
    try {
      const response = await fetch(`https://refulgently-unavailing-mathilda.ngrok-free.dev/api/cart/${cartItemId}`, {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${localStorage.getItem('token') || ''}`,
          'ngrok-skip-browser-warning': 'true'
        }
      });

      if (response.ok) {
        const updatedCart = cartItems.filter(item => item.id !== cartItemId);
        setCartItems(updatedCart);
        localStorage.setItem('cart', JSON.stringify(updatedCart));
        if (updatedCart.length === 0) setIsSaved(false);
      }
    } catch (err) {
      console.error("刪除失敗:", err);
    }
  };

  // 💾 同步儲存至後端資料庫
  const handleSaveToDatabase = async () => {
    if (cartItems.length === 0 || !currentUserId) return;
    try {
      setLoading(true);
      for (const item of cartItems) {
        const realProductId = item.product_id || item.id;
        await fetch('https://refulgently-unavailing-mathilda.ngrok-free.dev/api/cart', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('token') || ''}`,
            'ngrok-skip-browser-warning': 'true'
          },
          body: JSON.stringify({
            user_id: Number(currentUserId),   // 動態綁定，不管使用者有多少人都沒問題
            product_id: Number(realProductId) 
          })
        });
      }
      alert("🎉 配置清單已成功同步至資料庫！");
      setIsSaved(true);
      window.location.reload(); 
    } catch (err) {
      console.error("儲存失敗:", err);
    } finally {
      setLoading(false);
    }
  };

  const totalPrice = cartItems.reduce((sum, item) => sum + (item.price || item.productPrice || 0), 0);

  return (
    <div className="cart-container">
      <div className="cart-header">
        <h1><ShoppingBag /> 我的配置清單</h1>
        {currentUserId ? (
          <p style={{ color: '#16a34a', fontWeight: 'bold' }}>👤 當前帳號 ID: {currentUserId}</p>
        ) : (
          <p style={{ color: '#dc2626', fontWeight: 'bold' }}>⚠️ 請先登入系統才能進行配置</p>
        )}
      </div>

      {currentUserId && cartItems.length > 0 ? (
        <div className="cart-content">
          {/* ...中間的 cart-list 排版全部保留... */}
          <div className="cart-list">
            {cartItems.map(item => (
              <div key={item.id} className="cart-item">
                <img src={item.image || item.image_url || 'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=500'} alt={item.name} />
                <div className="item-info">
                  <h3>{item.name}</h3>
                  <p>尺寸：{item.length_cm || '-'} x {item.width || '-'} x {item.height || '-'} cm</p>
                  <p className="item-price">NT$ {(item.price || 0).toLocaleString()}</p>
                </div>
                <button className="remove-btn" onClick={() => removeItem(item.id)}>
                  <Trash2 size={20} />
                </button>
              </div>
            ))}
          </div>

          <div className="cart-summary">
            <h3>預計總額</h3>
            <div className="summary-row">
              <span>商品數量</span>
              <span>{cartItems.length} 件</span>
            </div>
            <div className="summary-row total">
              <span>總計</span>
              <span>NT$ {totalPrice.toLocaleString()}</span>
            </div>
            <div className="cart-action-area">
              <button className="save-btn" onClick={handleSaveToDatabase} disabled={loading}>
                {loading ? "⏳ 正在儲存..." : "💾 儲存配置清單"}
              </button>
              {isSaved && (
                <button className="checkout-btn" onClick={() => window.location.href=`/vr-scene?userId=${currentUserId}`}>
                  進入虛擬場景配置 <ChevronRight size={18} />
                </button>
              )}
            </div>
          </div>
        </div>
      ) : (
        <div className="empty-cart">
          <p>{currentUserId ? "這個帳號的清單目前是空的，快去型錄選購吧！" : "請登入後查看配置清單"}</p>
          <button onClick={() => window.location.href='/catalog'}>前往型錄</button>
        </div>
      )}
    </div>
  );
};

export default Cart;