import React, { useState, useEffect } from 'react';
import { Trash2, Send, PackageOpen, PlusCircle, Search, Copy, Image } from 'lucide-react';
import './Projects.css';
import { showToast, showConfirm } from '../../components/Ui/ui';
import ProjectMedia from '../../components/ProjectMedia/ProjectMedia';

const API_BASE = 'http://163.13.202.116:5050';

const getHeaders = {
};

const mutateHeaders = {
  'Content-Type': 'application/json',
};

const getSyncCode = (project) =>
  project?.sync_code || String(project?.id ?? '').padStart(5, '0');

const Projects = () => {
  const [projects, setProjects] = useState([]);
  const [loading, setLoading] = useState(false);
  const [expandedIds, setExpandedIds] = useState([]);
  const [mediaOpenIds, setMediaOpenIds] = useState([]);
  const [furnitureMap, setFurnitureMap] = useState({});
  const [vrModalProject, setVrModalProject] = useState(null);
  const [searchTerm, setSearchTerm] = useState('');
  const [renaming, setRenaming] = useState(null);

  const currentUserId = localStorage.getItem('user_id');

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
        setProjects(body.data || []);
      }
    } catch (err) {
      console.error('載入專案失敗:', err);
    } finally {
      setLoading(false);
    }
  };

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

  const saveRename = async (project) => {
    const newName = (renaming?.value || '').trim();
    setRenaming(null);
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
      fetchProjects();
    } catch (err) {
      console.error('複製專案失敗:', err);
      showToast(`複製失敗:${err.message}`, 'error');
    }
  };

  const toggleExpand = (id) => {
    setExpandedIds(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    );
  };

  const toggleMedia = (id) => {
    setMediaOpenIds(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    );
  };

  const deleteProject = async (id) => {
    if (!await showConfirm({ message: '確定要刪除這個專案嗎？', danger: true })) return;
    try {
      const res = await fetch(`${API_BASE}/api/projects/${id}`, {
        method: 'DELETE',
        headers: mutateHeaders,
      });
      if (!res.ok) throw new Error(`伺服器回應 ${res.status}`);
      setProjects(prev => prev.filter(p => p.id !== id));
      showToast('專案已刪除', 'success');
    } catch (err) {
      console.error('刪除專案失敗:', err);
      showToast('刪除專案失敗，請稍後再試', 'error');
    }
  };

  const confirmToVR = async (project) => {
    try {
      const res = await fetch(`${API_BASE}/api/projects/${project.id}/confirm`, {
        method: 'PATCH',
        headers: mutateHeaders,
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || `伺服器回應 ${res.status}`);
      }

      let latest = project;
      try {
        const listRes = await fetch(
          `${API_BASE}/api/projects?userId=${currentUserId}`,
          { headers: getHeaders }
        );
        if (listRes.ok) {
          const body = await listRes.json();
          const list = body.data || [];
          setProjects(list);
          const found = list.find(p => String(p.id) === String(project.id));
          if (found) latest = found;
        }
      } catch (e) {
        console.error('重新載入專案以取得 sync_code 失敗:', e);
      }

      setVrModalProject(latest);
    } catch (err) {
      console.error('送到 VR 失敗:', err);
      showToast(`傳送至 VR 失敗：${err.message}`, 'error');
    }
  };

  const goAddFurniture = (project) => {
    localStorage.setItem('editProjectId', project.id);
    localStorage.setItem('editProjectName', project.name);
    window.location.href = '/catalog';
  };

  const formatDate = (raw) => {
    if (!raw) return '';
    const d = new Date(raw);
    return isNaN(d) ? raw : d.toLocaleString('zh-TW');
  };

  const filteredProjects = projects.filter(project => {
    const keyword = searchTerm.trim().toLowerCase();
    if (!keyword) return true;
    const name = (project.name || '').toLowerCase();
    const code = getSyncCode(project);
    return (
      name.includes(keyword) ||
      String(project.id).includes(keyword) ||
      code.includes(keyword)
    );
  });

  const getFurnitureId = (item) =>
    item.furniture_id ?? item.id ?? item.product_id ?? item.furnitureId;

  const getItemPrice = (item) => {
    return Number(furnitureMap[getFurnitureId(item)]?.price || 0);
  };

  const getProjectTotal = (items) =>
    items.reduce((sum, item) => sum + getItemPrice(item), 0);

  const groupItems = (items) => {
    const map = {};
    items.forEach(item => {
      const fid = getFurnitureId(item);
      const key = fid === undefined || fid === null ? 'unknown' : String(fid);
      if (!map[key]) {
        map[key] = { fid, count: 0, sample: item };
      }
      map[key].count += 1;
    });
    return Object.values(map);
  };

  if (!currentUserId) {
    return (
      <div className="projects-container">
        <p className="projects-login-warn">⚠️ 請先登入才能查看</p>
      </div>
    );
  }

  return (
    <div className="projects-container">
      <div className="projects-header">
        <h1><PackageOpen /> 我的專案</h1>
      </div>

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
          const isMediaOpen = mediaOpenIds.includes(project.id);
          const items = Array.isArray(project.items) ? project.items : [];
          const isConfirmed = project.status === 'confirmed';

          return (
            <div
              key={project.id}
              className={`project-card ${isConfirmed ? 'confirmed' : ''}`}
            >
              <div className="project-card-header">
                <div className="project-card-header-row">
                  <div className="project-card-title">
                    <span className="project-id">
                      #{getSyncCode(project)}
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
                      <span className="badge-confirmed">已傳送至 VR</span>
                    )}
                  </div>

                  <div className="project-total-price">
                    <span className="project-total-label">專案總金額</span>
                    <span className="project-total-value">
                      NT$ {getProjectTotal(items).toLocaleString()}
                    </span>
                  </div>
                </div>

                <div className="project-card-meta">
                  <span>建立時間：{formatDate(project.created_at)}</span>
                  <span>內含 {items.length} 件家具</span>
                </div>

                <div className="project-card-actions">
                  <button
                    className="btn-secondary"
                    onClick={() => toggleExpand(project.id)}
                  >
                    {isExpanded ? '關閉家具清單' : '展開家具清單'}
                  </button>

                  <button
                    className="btn-secondary"
                    onClick={() => toggleMedia(project.id)}
                    title="查看 VR 實景截圖"
                  >
                    <Image size={16} /> {isMediaOpen ? '關閉截圖' : '查看截圖'}
                  </button>

                  <button
                    className="btn-vr"
                    onClick={() => confirmToVR(project)}
                  >
                    <Send size={16} /> 傳送至 VR
                  </button>

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

              {isExpanded && (
                <div className="project-panel">
                  <div className="project-panel-top">
                    <span className="panel-title">專案配置清單</span>
                    <button
                      className="btn-add-furniture"
                      onClick={() => goAddFurniture(project)}
                    >
                      <PlusCircle size={16} /> 修改專案
                    </button>
                  </div>

                  <p className="panel-subtitle">
                    此清單僅供檢視，如需新增或刪減家具，請點右上角「修改專案」：
                  </p>

                  {items.length === 0 ? (
                    <p className="panel-empty">
                      此房間目前空蕩蕩，趕快點上方按鈕加入一些家具吧！
                    </p>
                  ) : (
                    <div className="panel-furniture-list">
                      {groupItems(items).map(({ fid, count, sample }) => {
                        const furnitureInfo = furnitureMap[fid];
                        const displayName =
                          furnitureInfo?.name ||
                          sample.name ||
                          (fid !== undefined && fid !== null ? `家具 ID: ${fid}` : '未知家具');

                        const unitPrice = Number(furnitureInfo?.price || 0);
                        const subtotal = unitPrice * count;
                        return (
                          <div key={`${fid}`} className="panel-furniture-item">
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
                            <span className="furniture-unit">
                              NT$ {unitPrice.toLocaleString()}
                              <span className="furniture-times"> × {count}</span>
                            </span>
                            <span className="furniture-subtotal">
                              NT$ {subtotal.toLocaleString()}
                            </span>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              )}

              {isMediaOpen && <ProjectMedia projectId={project.id} />}
            </div>
          );
        })}
      </div>

      {vrModalProject && (
        <div className="vr-modal-overlay" onClick={() => setVrModalProject(null)}>
          <div className="vr-modal-box" onClick={(e) => e.stopPropagation()}>
            <div className="vr-modal-success">已同步到 VR</div>
            <p className="vr-modal-subtitle">
              「{vrModalProject.name}」的最新配置已送出
            </p>

            <p className="vr-modal-label">請在 VR 眼鏡輸入以下同步代碼查看</p>
            <div className="vr-modal-code">
              {getSyncCode(vrModalProject)}
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