// src/components/ProjectMedia/ProjectMedia.js
// 🖼 專案 VR 實景截圖:展開後才載入清單,支援大圖檢視(可左右切換)與刪除
import React, { useState, useEffect, useCallback } from 'react';
import { Trash2, X, ChevronLeft, ChevronRight, ImageIcon } from 'lucide-react';
import { showToast, showConfirm } from '../Ui/ui';
import './ProjectMedia.css';

const API_BASE = 'http://163.13.202.116:5050';

const ProjectMedia = ({ projectId }) => {
  const [media, setMedia] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  // 大圖檢視:目前看的是第幾張(null = 沒開)
  const [viewerIndex, setViewerIndex] = useState(null);

  // ── 載入截圖清單(元件被展開時才會掛載,等於「展開才抓」)──
  useEffect(() => {
    let cancelled = false;
    const fetchMedia = async () => {
      try {
        setLoading(true);
        setError(null);
        const res = await fetch(`${API_BASE}/api/projects/${projectId}/media`);
        if (!res.ok) throw new Error(`伺服器回應 ${res.status}`);
        const body = await res.json();
        if (!cancelled) setMedia(Array.isArray(body.data) ? body.data : []);
      } catch (err) {
        console.error('載入截圖失敗:', err);
        if (!cancelled) setError('截圖載入失敗,請稍後再試');
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    fetchMedia();
    return () => { cancelled = true; };
  }, [projectId]);

  // ── 刪除單張截圖 ──
  const deleteMedia = async (mediaId, e) => {
    e.stopPropagation(); // 避免觸發開大圖
    const confirmed = await showConfirm({
      title: '刪除截圖',
      message: '確定要刪除這張截圖嗎?刪除後無法復原。',
      danger: true,
    });
    if (!confirmed) return;

    try {
      const res = await fetch(`${API_BASE}/api/projects/${projectId}/media/${mediaId}`, {
        method: 'DELETE',
      });
      const body = await res.json().catch(() => ({}));
      if (!res.ok || body.success === false) {
        throw new Error(body.message || '刪除失敗');
      }
      // 從畫面即時移除,不重新整理
      setMedia(prev => prev.filter(m => m.id !== mediaId));
      showToast(body.message || '截圖已刪除', 'success');
    } catch (err) {
      console.error('刪除截圖失敗:', err);
      showToast(`刪除失敗:${err.message}`, 'error');
    }
  };

  // ── 大圖左右切換 ──
  const showPrev = useCallback((e) => {
    e?.stopPropagation();
    setViewerIndex(i => (i > 0 ? i - 1 : media.length - 1));
  }, [media.length]);

  const showNext = useCallback((e) => {
    e?.stopPropagation();
    setViewerIndex(i => (i < media.length - 1 ? i + 1 : 0));
  }, [media.length]);

  // 鍵盤操作:← → 切換、Esc 關閉
  useEffect(() => {
    if (viewerIndex === null) return;
    const onKey = (e) => {
      if (e.key === 'Escape') setViewerIndex(null);
      if (e.key === 'ArrowLeft') showPrev();
      if (e.key === 'ArrowRight') showNext();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [viewerIndex, showPrev, showNext]);

  const formatTime = (raw) => {
    if (!raw) return '';
    const d = new Date(raw.replace(' ', 'T'));
    return isNaN(d) ? raw : d.toLocaleString('zh-TW', {
      month: 'numeric', day: 'numeric',
      hour: '2-digit', minute: '2-digit',
    });
  };

  return (
    <div className="media-panel">
      <div className="media-panel-title">
        <ImageIcon size={16} /> VR 實景截圖
        {!loading && media.length > 0 && (
          <span className="media-count">{media.length} 張</span>
        )}
      </div>

      {loading ? (
        <div className="loading-wrap">
          <span className="loading-spinner" />截圖載入中...
        </div>
      ) : error ? (
        <p className="media-empty media-error">{error}</p>
      ) : media.length === 0 ? (
        <p className="media-empty">尚無截圖,在 VR 眼鏡中截圖後會顯示在這裡</p>
      ) : (
        <div className="media-grid">
          {media.map((m, idx) => (
            <div key={m.id} className="media-item" onClick={() => setViewerIndex(idx)}>
              <div className="media-thumb-wrap">
                <img src={m.url} alt={`專案截圖 ${idx + 1}`} loading="lazy" />
                <button
                  className="media-delete"
                  onClick={(e) => deleteMedia(m.id, e)}
                  aria-label="刪除這張截圖"
                  title="刪除截圖"
                >
                  <Trash2 size={14} />
                </button>
              </div>
              <span className="media-time">{formatTime(m.created_at)}</span>
            </div>
          ))}
        </div>
      )}

      {/* ── 大圖檢視 ── */}
      {viewerIndex !== null && media[viewerIndex] && (
        <div className="media-viewer" onClick={() => setViewerIndex(null)}>
          <button
            className="media-viewer-close"
            onClick={() => setViewerIndex(null)}
            aria-label="關閉"
          >
            <X size={24} />
          </button>

          {media.length > 1 && (
            <button className="media-viewer-nav prev" onClick={showPrev} aria-label="上一張">
              <ChevronLeft size={28} />
            </button>
          )}

          <div className="media-viewer-content" onClick={e => e.stopPropagation()}>
            <img src={media[viewerIndex].url} alt={`專案截圖 ${viewerIndex + 1}`} />
            <div className="media-viewer-info">
              <span>{formatTime(media[viewerIndex].created_at)}</span>
              <span className="media-viewer-count">
                {viewerIndex + 1} / {media.length}
              </span>
            </div>
          </div>

          {media.length > 1 && (
            <button className="media-viewer-nav next" onClick={showNext} aria-label="下一張">
              <ChevronRight size={28} />
            </button>
          )}
        </div>
      )}
    </div>
  );
};

export default ProjectMedia;