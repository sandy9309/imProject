// src/pages/Guide/GuidePage.js
// 📖 使用說明頁(/guide):隨時可以回來查閱的完整版手冊
import React, { useState } from 'react';
import {
  BookOpen, Layout, ShoppingCart, Save, Send,
  Glasses, Move3d, Camera, PlayCircle, Sparkles, Folder, User,
} from 'lucide-react';
import { GUIDE_STEPS } from '../../components/Guide/guideSteps';
import { GUIDE_FEATURES } from '../../components/Guide/guideFeatures';
import GuideTour from '../../components/Guide/GuideTour';
import '../../components/Guide/Guide.css';

const ICONS = {
  Layout, ShoppingCart, Save, Send, Glasses, Move3d, Camera,
  Sparkles, Folder, User,
};

const GuidePage = () => {
  const [replayTour, setReplayTour] = useState(false);
  // 展開中的功能區塊(預設全部收合,點標題展開)
  const [openFeature, setOpenFeature] = useState(null);

  return (
    <div className="guide-page">
      <div className="guide-page-header">
        <h1><BookOpen size={26} /> 使用說明</h1>
        <p>從挑家具到戴上眼鏡看見成果,只要幾個步驟。</p>
        <button className="guide-replay-btn" onClick={() => setReplayTour(true)}>
          <PlayCircle size={16} /> 重新播放新手導覽
        </button>
      </div>

      {/* ══ 整體流程 7 步 ══ */}
      <h2 className="guide-section-title">整體流程</h2>
      <div className="guide-steps-list">
        {GUIDE_STEPS.map((s, i) => {
          const Icon = ICONS[s.icon] || Layout;
          return (
            <div key={i} className="guide-step-row">
              <div className="guide-step-num">
                <span>{i + 1}</span>
                {i < GUIDE_STEPS.length - 1 && <div className="guide-step-line" />}
              </div>
              <div className="guide-step-body">
                <div className="guide-step-head">
                  <Icon size={20} />
                  <h3>{s.title}</h3>
                </div>
                <p className="guide-step-sub">{s.subtitle}</p>
                <p className="guide-step-desc">{s.desc}</p>
                {s.tip && <p className="guide-tip">{s.tip}</p>}
              </div>
            </div>
          );
        })}
      </div>

      {/* ══ 網頁功能詳解(可展開/收合) ══ */}
      <h2 className="guide-section-title">網頁功能詳解</h2>
      <p className="guide-section-hint">點各項目可展開細節說明。</p>

      <div className="guide-feature-list">
        {GUIDE_FEATURES.map((f, i) => {
          const Icon = ICONS[f.icon] || Layout;
          const isOpen = openFeature === i;
          return (
            <div key={i} className={`guide-feature ${isOpen ? 'open' : ''}`}>
              <button
                className="guide-feature-head"
                onClick={() => setOpenFeature(isOpen ? null : i)}
              >
                <span className="guide-feature-icon"><Icon size={18} /></span>
                <span className="guide-feature-name">{f.name}</span>
                <span className="guide-feature-arrow">{isOpen ? '−' : '+'}</span>
              </button>

              {isOpen && (
                <div className="guide-feature-body">
                  <p className="guide-feature-intro">{f.intro}</p>
                  <dl className="guide-feature-items">
                    {f.items.map((it, j) => (
                      <div key={j} className="guide-feature-item">
                        <dt>{it.t}</dt>
                        <dd>{it.d}</dd>
                      </div>
                    ))}
                  </dl>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* ══ 常見問題 ══ */}
      <div className="guide-faq">
        <h2>常見問題</h2>

        <div className="guide-faq-item">
          <h4>編碼可以給別人看嗎?</h4>
          <p>可以!只要知道編碼的人,戴上眼鏡都能看到同一個配置,很適合跟家人或室友一起討論。</p>
        </div>

        <div className="guide-faq-item">
          <h4>改了配置之後,眼鏡會自動更新嗎?</h4>
          <p>不會,修改後請回到「我的專案」再按一次「送到 VR」,眼鏡才會拿到最新版本。</p>
        </div>

        <div className="guide-faq-item">
          <h4>為什麼網頁上不能拖拉家具位置?</h4>
          <p>擺放位置是在眼鏡裡調整的。網頁負責「挑選要試哪些家具」,實際怎麼擺、擺哪裡,戴上眼鏡用手把移動最直觀,也能立刻看出尺寸合不合適。</p>
        </div>

        {/* <div className="guide-faq-item">
          <h4>沒有 Quest 3 眼鏡怎麼辦?</h4>
          <p>本系統的體驗設備由系辦提供借用,可洽系辦借用 Meta Quest 3 進行體驗。</p>
        </div> */}

        <div className="guide-faq-item">
          <h4>配置清單和專案有什麼不同?</h4>
          <p>配置清單是「還在挑選中」的暫存區;儲存後就變成「專案」,才會有編碼、才能送到 VR。</p>
        </div>

        <div className="guide-faq-item">
          <h4>配置清單的內容會保存嗎?</h4>
          <p>配置清單暫存在你目前使用的瀏覽器中。若清除瀏覽器資料或換一台裝置,清單內容不會同步,建議挑選完成後盡快儲存成專案。</p>
        </div>
      </div>

      {replayTour && (
        <GuideTour forceOpen onClose={() => setReplayTour(false)} />
      )}
    </div>
  );
};

export default GuidePage;