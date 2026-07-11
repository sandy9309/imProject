import React, { useState, useEffect } from 'react';
import { Trash2, Send, PackageOpen, PlusCircle, Search, Copy } from 'lucide-react';
import './Projects.css';
import { showToast, showConfirm } from '../../components/Ui/ui';

// 🌐 學校伺服器的正式內網 IP 網址
const API_BASE = 'http://163.13.202.116:5050';

// 💡 本地撈取通常不需額外 header，若後端有其他防護可再依需求加入
const getHeaders = {
};

const mutateHeaders = {
  'Content-Type': 'application/json',
};

const Projects = () => {
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(false);
  const [expandedIds, setExpandedIds] = useState([]);
  // 🚀 家具對照表：{ [家具ID]: { name, price, ... } }，用來把 furniture_id 轉換成真實名稱
  const [furnitureMap, setFurnitureMap] = useState({});
  // 🚀 VR 編碼彈窗：{ id, name } 或 null。有值時彈窗顯示，關閉時設回 null
  const [vrModalProject, setVrModalProject] = useState(null);
  // 🚀 專案搜尋關鍵字
  const [searchTerm, setSearchTerm] = useState('');
  // 🚀 改名中的專案:{ id, value } 或 null
  const [renaming, setRenaming] = useState(null);

  const currentUserId = localStorage.getItem('user_id');

  // ── 載入專案列表 ────────────────────────────────────────────
  const fetchProjects = async () => {
    if (!currentUserId) return;
    try {
      setLoading(true);
      const res = await fetch(
        `${API_BASE}/api/projects?userId=${currentUserId}`,
        { headers: getHeaders }
      );
      if (res.ok) {
        const body = await res.json();
        // 在 F12 檢查學校伺服器送來的專案資料結構
        console.log('學校伺服器回傳的原始專案資料：', body);
        setProjects(body.data || []);
      }
    } catch (err) {
      console.error('載入專案失敗:', err);
    } finally {
      setLoading(false);
    }
  };

  // ── 載入家具型錄，建立 id -> 家具資料 的對照表 ──────────────
  const fetchFurnitureMap = async () => {
    try {
      const res = await fetch(`${API_BASE}/api/furnitures`, { headers: getHeaders });
      if (res.ok) {
        const data = await res.json();
        const list = Array.isArray(data) ? data : (data.data || []);
        const map = {};
        list.forEach(f => { map[f.id] = f; });
        setFurnitureMap(map);
      }
    } catch (err) {
      console.error('載入家具型錄失敗:', err);
    }
  };

  useEffect(() => { 
    fetchProjects(); 
    fetchFurnitureMap();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentUserId]);

  // ── 修改專案名稱(單擊名稱進入編輯,Enter或失焦時儲存,Esc取消)──
  const saveRename = async (project) => {
    const newName = (renaming?.value || '').trim();
    setRenaming(null);
    // 空白或沒改就不送出
    if (!newName || newName === project.name) return;

    try {
      const items = Array.isArray(project.items) ? project.items : [];
      const res = await fetch(`${API_BASE}/api/projects/${project.id}`, {
        method: 'PUT',
        headers: mutateHeaders,
        body: JSON.stringify({
          name: newName,
          itemsRaw: JSON.stringify(items),
        }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '改名失敗');
      }
      setProjects(prev =>
        prev.map(p => (p.id === project.id ? { ...p, name: newName } : p))
      );
    } catch (err) {
      console.error('修改專案名稱失敗:', err);
      showToast(`改名失敗:${err.message}`, 'error');
    }
  };

  // ── 複製專案(建立全新專案,編碼由資料庫給新的,名稱加上【複製】)──
  const duplicateProject = async (project) => {
    try {
      const items = Array.isArray(project.items) ? project.items : [];
      const res = await fetch(`${API_BASE}/api/projects`, {
        method: 'POST',
        headers: mutateHeaders,
        body: JSON.stringify({
          user_id: Number(currentUserId),
          name: `${project.name}【複製】`,
          l: project.l ?? null,
          w: project.w ?? null,
          itemsRaw: JSON.stringify(items),
        }),
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || '複製專案失敗');
      }
      // 重新載入列表,新專案(帶新編碼)就會出現
      fetchProjects();
    } catch (err) {
      console.error('複製專案失敗:', err);
      showToast(`複製失敗:${err.message}`, 'error');
    }
  };

  // ── 展開 / 收合面板 ─────────────────────────────────────────
  const toggleExpand = (id) => {
    setExpandedIds(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    );
  };

  // ── 刪除單件家具 ─────────────────────────────────────────────
  // 🚀 暫時補丁：後端 items 目前每筆的 furniture_id 都抓不到值（全部是 undefined），
  // 如果照 ID 比對來刪除，會把所有 ID 相同（都是 undefined）的項目一起誤刪。
  // 因此改用「陣列位置（第幾筆）」來精準指定要刪除哪一筆，跟後端資料是否修好無關。
  const removeFurnitureFromProject = async (project, targetIndex) => {
    // 🚀 刪除前先跟使用者確認,避免手滑誤刪
    const confirmed = await showConfirm({
      message: '確定要從專案中移除這件家具嗎？',
      danger: true,
    });
    if (!confirmed) return;

    const currentItems = Array.isArray(project.items) ? project.items : [];
    const newItems = currentItems.filter((_, idx) => idx !== targetIndex);
    try {
      const res = await fetch(`${API_BASE}/api/projects/${project.id}`, {
        method: 'PUT',
        headers: mutateHeaders,
        body: JSON.stringify({ name: project.name, itemsRaw: JSON.stringify(newItems) }),
      });
      if (res.ok) {
        setProjects(prev =>
          prev.map(p =>
            p.id === project.id ? { ...p, items: newItems } : p
          )
        );
      }
    } catch (err) {
      console.error('移除家具失敗:', err);
    }
  };

  // ── 刪除整個專案 ─────────────────────────────────────────────
  const deleteProject = async (id) => {
    if (!await showConfirm({ message: '確定要刪除這個專案嗎？', danger: true })) return;
    try {
      await fetch(`${API_BASE}/api/projects/${id}`, {
        method: 'DELETE',
        headers: mutateHeaders,
      });
      setProjects(prev => prev.filter(p => p.id !== id));
    } catch (err) {
      console.error('刪除專案失敗:', err);
    }
  };

  // ── 送到 VR ──────────────────────────────────────────────────
  // 🚀 不管內容有沒有變動都能執行；成功後彈出編碼視窗，方便使用者隨時查看/重新確認編碼
  const confirmToVR = async (project) => {
    try {
      const res = await fetch(`${API_BASE}/api/projects/${project.id}/confirm`, {
        method: 'PATCH',
        headers: mutateHeaders,
      });
      if (res.ok) {
        setVrModalProject(project);
        fetchProjects();
      }
    } catch (err) {
      console.error('送到 VR 失敗:', err);
    }
  };

  // ── 跳到型錄新增家具 ─────────────────────────────────────────
  const goAddFurniture = (project) => {
    localStorage.setItem('editProjectId', project.id);
    localStorage.setItem('editProjectName', project.name);
    window.location.href = '/catalog';
  };

  // ── 格式化日期 ───────────────────────────────────────────────
  const formatDate = (raw) => {
    if (!raw) return '';
    const d = new Date(raw);
    return isNaN(d) ? raw : d.toLocaleString('zh-TW');
  };

  // 🚀 搜尋過濾：專案名稱 或 編號（例如輸入 34 或 00034 都能找到 #00034）
  const filteredProjects = projects.filter(project => {
    const keyword = searchTerm.trim().toLowerCase();
    if (!keyword) return true;
    const name = (project.name || '').toLowerCase();
    const idPadded = String(project.id).padStart(5, '0');
    return (
      name.includes(keyword) ||
      String(project.id).includes(keyword) ||
      idPadded.includes(keyword)
    );
  });

  // 🚀 計算單件家具價格（透過對照表查詢，查不到回傳 0）
  const getItemPrice = (item) => {
    const furnitureId =
      item.furniture_id ?? item.id ?? item.product_id ?? item.furnitureId;
    return Number(furnitureMap[furnitureId]?.price || 0);
  };

  // 🚀 計算整個專案的總金額
  const getProjectTotal = (items) =>
    items.reduce((sum, item) => sum + getItemPrice(item), 0);

  if (!currentUserId) {
    return (
      <div className="projects-container">
        <p className="projects-login-warn">⚠️ 請先登入才能查看專案</p>
      </div>
    );
  }

  return (
    <div className="projects-container">
      <div className="projects-header">
        <h1><PackageOpen /> 我的專案</h1>
      </div>

      {/* 🚀 專案搜尋框：可用專案名稱或編號快速搜尋 */}
      <div className="projects-search-bar">
        <Search size={18} className="projects-search-icon" />
        <input
          type="text"
          placeholder="搜尋專案名稱或編號..."
          value={searchTerm}
          onChange={e => setSearchTerm(e.target.value)}
        />
      </div>

      {loading && (<div className="loading-wrap"><span className="loading-spinner" />載入專案中...</div>)}

      {!loading && projects.length === 0 && (
        <div className="projects-empty">
          <p>目前沒有任何專案，先去配置清單儲存一個吧！</p>
          <button onClick={() => window.location.href = '/cart'}>
            前往配置清單
          </button>
        </div>
      )}

      {!loading && projects.length > 0 && filteredProjects.length === 0 && (
        <div className="projects-empty">
          <p>找不到符合「{searchTerm}」的專案</p>
        </div>
      )}

      <div className="projects-list">
        {filteredProjects.map(project => {
          const isExpanded = expandedIds.includes(project.id);
          const items = Array.isArray(project.items) ? project.items : [];
          const isConfirmed = project.status === 'confirmed';

          return (
            <div
              key={project.id}
              className={`project-card ${isConfirmed ? 'confirmed' : ''}`}
            >
              {/* ── 專案標題列 ── */}
              <div className="project-card-header">
                <div className="project-card-header-row">
                  <div className="project-card-title">
                    <span className="project-id">
                      #{String(project.id).padStart(5, '0')}
                    </span>
                    {renaming?.id === project.id ? (
                      <input
                        className="project-rename-input"
                        autoFocus
                        value={renaming.value}
                        onChange={e => setRenaming({ id: project.id, value: e.target.value })}
                        onBlur={() => saveRename(project)}
                        onKeyDown={e => {
                          if (e.key === 'Enter') e.target.blur();
                          if (e.key === 'Escape') setRenaming(null);
                        }}
                        maxLength={30}
                      />
                    ) : (
                      <h2
                        className="project-name-editable"
                        title="點擊修改專案名稱"
                        onClick={() => setRenaming({ id: project.id, value: project.name })}
                      >
                        {project.name}
                      </h2>
                    )}
                    {isConfirmed && (
                      <span className="badge-confirmed">已送到 VR</span>
                    )}
                  </div>

                  {/* 🚀 專案總金額（右上角） */}
                  <div className="project-total-price">
                    <span className="project-total-label">專案總金額</span>
                    <span className="project-total-value">
                      NT$ {getProjectTotal(items).toLocaleString()}
                    </span>
                  </div>
                </div>

                <div className="project-card-meta">
                  <span>建立時間：{formatDate(project.created_at)}</span>
                  <span>內含 {items.length} 件模擬家具</span>
                </div>

                <div className="project-card-actions">
                  <button
                    className="btn-secondary"
                    onClick={() => toggleExpand(project.id)}
                  >
                    {isExpanded ? '⚙️ 關閉家具清單' : '⚙️ 展開家具清單'}
                  </button>

                  {/* 🚀 不管有無變動、不管是否已確認過，都能按，且每次都會彈出編碼視窗 */}
                  <button
                    className="btn-vr"
                    onClick={() => confirmToVR(project)}
                  >
                    <Send size={16} /> 送到 VR
                  </button>

                  {/* 🚀 複製專案:建立一份全新專案(新編碼),名稱自動加上【複製】 */}
                  <button
                    className="btn-secondary"
                    onClick={() => duplicateProject(project)}
                    title="複製此專案"
                  >
                    <Copy size={16} /> 複製
                  </button>

                  <button
                    className="btn-delete-project"
                    onClick={() => deleteProject(project.id)}
                  >
                    <Trash2 size={16} />
                  </button>
                </div>
              </div>

              {/* ── 展開：家具清單 + 新增按鈕 ── */}
              {isExpanded && (
                <div className="project-panel">
                  <div className="project-panel-top">
                    <span className="panel-title">⚙️ 專案配置精細調整面板</span>
                    {/* 🚀 拿掉 isConfirmed 限制：已送出的專案一樣可以繼續調整，改完再重送即可 */}
                    <button
                      className="btn-add-furniture"
                      onClick={() => goAddFurniture(project)}
                    >
                      <PlusCircle size={16} /> 修改專案
                    </button>
                  </div>

                  <p className="panel-subtitle">
                    當前配置家具清單（點擊刪除鍵可從專案內移除）：
                  </p>

                  {items.length === 0 ? (
                    <p className="panel-empty">
                      此房間目前空蕩蕩，趕快點上方按鈕塞入一些家具吧！
                    </p>
                  ) : (
                    <div className="panel-furniture-list">
                      {items.map((item, idx) => {
                        // 🚀 容錯：不同版本後端可能用 furniture_id / id / product_id 存家具編號
                        const furnitureId =
                          item.furniture_id ?? item.id ?? item.product_id ?? item.furnitureId;
                        // 🚀 優先查對照表拿真實家具名稱，查不到才退回顯示 ID
                        const furnitureInfo = furnitureMap[furnitureId];
                        const displayName =
                          furnitureInfo?.name ||
                          item.name ||
                          (furnitureId !== undefined ? `家具 ID: ${furnitureId}` : '未知家具');

                        return (
                          <div key={idx} className="panel-furniture-item">
                            <img
                              className="panel-furniture-thumb"
                              src={
                                furnitureInfo?.image_url ||
                                'https://images.unsplash.com/photo-1538688525198-9b88f6f53126?w=200'
                              }
                              alt={displayName}
                            />
                            <span className="furniture-name">
                              {displayName}
                            </span>
                            {/* 🚀 單件家具價格 */}
                            <span className="furniture-price">
                              NT$ {getItemPrice(item).toLocaleString()}
                            </span>
                            {/* 🚀 拿掉 isConfirmed 限制；改用 idx（陣列位置）精準刪除單一項目 */}
                            <button
                              className="btn-remove-furniture"
                              onClick={() =>
                                removeFurnitureFromProject(project, idx)
                              }
                            >
                              <Trash2 size={15} />
                            </button>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* ── VR 編碼彈窗：按一次「送到 VR」就會顯示，隨時可以再按開啟查看 ── */}
      {vrModalProject && (
        <div className="vr-modal-overlay" onClick={() => setVrModalProject(null)}>
          <div className="vr-modal-box" onClick={(e) => e.stopPropagation()}>
            <div className="vr-modal-success">✅ 已同步到 VR</div>
            <p className="vr-modal-subtitle">
              「{vrModalProject.name}」的最新配置已送出
            </p>

            <p className="vr-modal-label">請在 VR 眼鏡輸入以下編碼查看</p>
            <div className="vr-modal-code">
              {String(vrModalProject.id).padStart(5, '0')}
            </div>

            <div className="vr-modal-actions">
              <button
                className="vr-modal-btn-close"
                onClick={() => setVrModalProject(null)}
              >
                關閉
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Projects;