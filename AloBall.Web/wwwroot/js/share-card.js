/**
 * AloBall EA FC 27 Player Card & Matchday Result Poster 1-Tap Share
 */
(function () {
    let htmlToImageLoaded = false;

    function ensureHtmlToImage() {
        if (window.htmlToImage) return Promise.resolve(window.htmlToImage);
        if (htmlToImageLoaded) {
            return new Promise((resolve) => {
                const interval = setInterval(() => {
                    if (window.htmlToImage) {
                        clearInterval(interval);
                        resolve(window.htmlToImage);
                    }
                }, 50);
            });
        }
        htmlToImageLoaded = true;
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/html-to-image/1.11.11/html-to-image.min.js';
            script.onload = () => resolve(window.htmlToImage);
            script.onerror = () => reject(new Error('Failed to load html-to-image'));
            document.head.appendChild(script);
        });
    }

    function escapeHtml(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function showToast(message, isWarning = false) {
        let toastEl = document.getElementById('aloball-share-toast');
        if (!toastEl) {
            toastEl = document.createElement('div');
            toastEl.id = 'aloball-share-toast';
            toastEl.style.cssText = 'position:fixed;bottom:24px;left:50%;transform:translateX(-50%);z-index:99999;padding:12px 24px;border-radius:30px;font-size:14px;font-weight:700;color:#fff;box-shadow:0 10px 25px rgba(0,0,0,0.5);transition:opacity 0.3s;pointer-events:none;text-align:center;max-width:90vw;';
            document.body.appendChild(toastEl);
        }
        toastEl.style.backgroundColor = isWarning ? '#eab308' : '#10b981';
        toastEl.textContent = message;
        toastEl.style.opacity = '1';
        setTimeout(() => { toastEl.style.opacity = '0'; }, 3800);
    }

    function getProxiedUrl(url) {
        if (!url) return '';
        if (url.startsWith('http://') || url.startsWith('https://')) {
            return '/api/share-card/img-proxy?url=' + encodeURIComponent(url);
        }
        return url;
    }

    function getOrCreateModal() {
        let modalEl = document.getElementById('aloballShareModal');
        if (!modalEl) {
            const html = `
            <div class="modal fade aloball-share-modal" id="aloballShareModal" tabindex="-1" aria-hidden="true">
                <div class="modal-dialog modal-dialog-centered modal-lg">
                    <div class="modal-content">
                        <div class="modal-header border-0 pb-0">
                            <h5 class="modal-title fw-bold" id="aloballShareModalTitle">EA FC 27 Ultimate Card</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="modal-body text-center p-3" id="aloballShareModalBody">
                            <div class="spinner-border text-primary my-5" role="status"></div>
                        </div>
                        <div class="modal-footer border-0 pt-0 d-flex justify-content-center gap-2">
                            <button type="button" class="btn btn-secondary px-4 fw-bold" style="min-height:44px;" data-bs-dismiss="modal">Đóng</button>
                            <button type="button" class="btn btn-success px-4 fw-bold d-flex align-items-center gap-2 shadow" style="min-height:44px;" id="aloballShareBtn">
                                <i class="bi bi-share-fill"></i> Chia sẻ 1-Chạm (Zalo / FB)
                            </button>
                        </div>
                    </div>
                </div>
            </div>`;
            document.body.insertAdjacentHTML('beforeend', html);
            modalEl = document.getElementById('aloballShareModal');
        }
        return new bootstrap.Modal(modalEl);
    }

    async function ensureCatalogLoaded() {
        if (window.fc27SvgMap && window.fc27CatalogMap) return;
        try {
            const catRes = await fetch('/api/playstyles/catalog');
            if (catRes.ok) {
                const catData = await catRes.json();
                window.fc27SvgMap = {};
                window.fc27CatalogMap = {};
                (catData.categories || []).forEach(c => {
                    (c.skills || []).forEach(s => {
                        window.fc27SvgMap[s.code] = s.svgPath;
                        window.fc27CatalogMap[s.code] = s;
                    });
                });
            }
        } catch (e) { /* fallback to API playStyleItems */ }
    }

    function buildLeftPlayStylesHtml(data) {
        let items = Array.isArray(data.playStyleItems) ? data.playStyleItems.slice() : [];
        if (items.length === 0 && data.playStyles && typeof data.playStyles === 'string') {
            const tokens = data.playStyles.split(',').map(t => t.trim()).filter(Boolean);
            tokens.sort((a, b) => (b.endsWith('+') ? 1 : 0) - (a.endsWith('+') ? 1 : 0));
            items = tokens.slice(0, 4).map(t => {
                const isPlus = t.endsWith('+');
                const code = t.replace('+', '').toUpperCase();
                const meta = window.fc27CatalogMap ? window.fc27CatalogMap[code] : null;
                return {
                    code: code,
                    nameVi: meta ? (meta.nameVi || meta.NameVi || code) : code,
                    nameEn: meta ? (meta.nameEn || meta.NameEn || '') : '',
                    svgPath: window.fc27SvgMap ? window.fc27SvgMap[code] : '',
                    isPlus: isPlus
                };
            }).filter(x => x.svgPath);
        }

        if (items.length === 0) return '';

        items.sort((a, b) => (b.isPlus ? 1 : 0) - (a.isPlus ? 1 : 0));
        items = items.slice(0, 4);

        let html = '<div class="fc-left-playstyles" aria-label="PlayStyles">';
        items.forEach(s => {
            if (!s.svgPath) return;
            const label = `${s.nameVi || s.code}${s.nameEn ? ' (' + s.nameEn + ')' : ''}${s.isPlus ? ' ★ PlayStyle+' : ''}`;
            const safeLabel = escapeHtml(label);
            html += `
                <span class="fc27-ps-item" title="${safeLabel}">
                    <span class="fc27-ps-diamond ${s.isPlus ? 'fc27-ps-gold' : 'fc27-ps-cyan'}">
                        <svg viewBox="0 0 24 24" class="fc27-ps-icon"><path d="${escapeHtml(s.svgPath)}" fill="currentColor"/></svg>
                    </span>
                    <span class="fc27-ps-hover-tooltip">${safeLabel}</span>
                </span>`;
        });
        html += '</div>';
        return html;
    }

    // 1. Open Player Card Modal
    window.openPlayerCardModal = async function (playerId) {
        const modal = getOrCreateModal();
        const titleEl = document.getElementById('aloballShareModalTitle');
        const bodyEl = document.getElementById('aloballShareModalBody');
        const shareBtn = document.getElementById('aloballShareBtn');

        titleEl.textContent = 'Thẻ Chỉ Số EA FC 27';
        bodyEl.innerHTML = '<div class="py-5"><div class="spinner-border text-warning" role="status"></div><div class="mt-2 text-muted small">Đang tính toán chỉ số EA FC 27...</div></div>';
        modal.show();

        try {
            const res = await fetch(`/api/share-card/player/${playerId}`);
            if (!res.ok) throw new Error('Không thể tải dữ liệu cầu thủ');
            const data = await res.json();

            if (!Array.isArray(data.playStyleItems) || data.playStyleItems.length === 0) {
                await ensureCatalogLoaded();
            }

            const leftPsHtml = buildLeftPlayStylesHtml(data);
            const photoSrc = getProxiedUrl(data.photoUrl);
            const logoSrc = getProxiedUrl(data.teamLogoUrl);

            bodyEl.innerHTML = `
                <div class="aloball-fut-card-wrapper">
                    <div class="aloball-fut-card tier-${escapeHtml(data.tier)}" id="targetPlayerCard">
                        <div class="fc-inner-plate">
                            <div class="fc-upper">
                                <div class="fc-left-rail">
                                    <div class="fc-badge">
                                        <div class="fc-ovr">${escapeHtml(data.stats.ovr)}</div>
                                        <div class="fc-pos">${escapeHtml(data.posCode)}</div>
                                        ${logoSrc ? `<img src="${escapeHtml(logoSrc)}" class="fc-crest" alt="crest" crossorigin="anonymous" />` : '<div class="fc-crest-placeholder">⚽</div>'}
                                    </div>
                                    ${leftPsHtml}
                                </div>
                                <div class="fc-portrait-box">
                                    ${photoSrc ? `<img src="${escapeHtml(photoSrc)}" class="fc-portrait" alt="player" crossorigin="anonymous" />` : '<div class="fc-portrait-avatar"><i class="bi bi-person-fill"></i></div>'}
                                </div>
                            </div>
                            <div class="fc-name-bar">
                                <h3 class="fc-name">${escapeHtml(data.displayName)}</h3>
                                <div class="fc-club-name">${escapeHtml(data.teamName)}</div>
                            </div>
                            <div class="fc-stats-grid">
                                <div class="fc-stat-item"><span class="fc-stat-val">${escapeHtml(data.stats.att)}</span><span class="fc-stat-label">ATT</span></div>
                                <div class="fc-stat-item"><span class="fc-stat-val">${escapeHtml(data.stats.cre)}</span><span class="fc-stat-label">CRE</span></div>
                                <div class="fc-stat-item"><span class="fc-stat-val">${escapeHtml(data.stats.sta)}</span><span class="fc-stat-label">STA</span></div>
                                <div class="fc-stat-item"><span class="fc-stat-val">${escapeHtml(data.stats.win)}</span><span class="fc-stat-label">WIN</span></div>
                                <div class="fc-stat-item"><span class="fc-stat-val">${escapeHtml(data.stats.rep)}</span><span class="fc-stat-label">REP</span></div>
                                <div class="fc-stat-item"><span class="fc-stat-val">${escapeHtml(data.stats.exp)}</span><span class="fc-stat-label">EXP</span></div>
                            </div>
                            <div class="fc-strip">
                                <span>Chân: <strong>${escapeHtml(data.preferredFoot || 'Bảo mật')}</strong></span>
                                <span class="fc-logo-badge">ALOBALL 27</span>
                                <span>${escapeHtml(data.region || 'Hà Nội')}</span>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            shareBtn.onclick = () => exportAndShare('targetPlayerCard', `${data.displayName}-FIFA27.png`, `Thẻ cầu thủ ${data.displayName} (${data.stats.ovr} OVR) - AloBall FC`);
        } catch (err) {
            bodyEl.innerHTML = `<div class="alert alert-danger my-4">${escapeHtml(err.message)}</div>`;
        }
    };

    function renderScorerListHtml(list) {
        if (!Array.isArray(list) || list.length === 0) {
            return '<div class="text-muted small fst-italic">Chưa có bàn thắng</div>';
        }
        return list.map(s => {
            const label = s.summaryText || `${s.playerName} (${s.goals}⚽)`;
            return `
                <div class="poster-scorer-row">
                    <span class="poster-scorer-name">${escapeHtml(label)}</span>
                    ${s.minutes ? `<span class="poster-scorer-minute">${escapeHtml(s.minutes)}</span>` : ''}
                </div>`;
        }).join('');
    }

    // 2. Open Match Poster Modal
    window.openMatchPosterModal = async function (matchId) {
        const modal = getOrCreateModal();
        const titleEl = document.getElementById('aloballShareModalTitle');
        const bodyEl = document.getElementById('aloballShareModalBody');
        const shareBtn = document.getElementById('aloballShareBtn');

        titleEl.textContent = 'Poster Kết Quả Trận Đấu';
        bodyEl.innerHTML = '<div class="py-5"><div class="spinner-border text-success" role="status"></div><div class="mt-2 text-muted small">Đang thiết lập poster phát sóng...</div></div>';
        modal.show();

        try {
            const res = await fetch(`/api/share-card/match/${matchId}`);
            if (!res.ok) throw new Error('Trận đấu chưa xác nhận kết quả hoặc không thể tạo poster.');
            const data = await res.json();

            const logoASrc = getProxiedUrl(data.teamA.logoUrl);
            const logoBSrc = getProxiedUrl(data.teamB.logoUrl);
            const coverSrc = getProxiedUrl(data.teamA.coverUrl || data.teamB.coverUrl);
            const motmPhotoSrc = data.motm && data.motm.photoUrl ? getProxiedUrl(data.motm.photoUrl) : '';

            const scorersAHtml = renderScorerListHtml(data.scorersA);
            const scorersBHtml = renderScorerListHtml(data.scorersB);

            bodyEl.innerHTML = `
                <div class="aloball-aspect-toggle mx-auto" style="max-width: 340px;">
                    <button type="button" class="active" id="btnAspectFeed" onclick="togglePosterAspect('feed')">
                        <i class="bi bi-grid-fill me-1"></i> Bảng Tin (4:5)
                    </button>
                    <button type="button" id="btnAspectStory" onclick="togglePosterAspect('story')">
                        <i class="bi bi-phone-fill me-1"></i> Story (9:16)
                    </button>
                </div>
                <div class="aloball-match-poster-wrapper">
                    <div class="aloball-match-poster ratio-feed" id="targetMatchPoster">
                        ${coverSrc ? `<div class="aloball-poster-backdrop" style="background-image: url('${escapeHtml(coverSrc)}'); opacity: ${Number(data.teamA.coverOpacity || 0.7)};"></div>` : ''}
                        <div class="aloball-poster-gradient"></div>
                        <div class="aloball-poster-content">
                            <div class="poster-header">
                                <span class="poster-category-tag">${escapeHtml(data.category)}</span>
                                <div class="poster-venue-time">
                                    <i class="bi bi-geo-alt-fill text-danger me-1"></i>${escapeHtml(data.venue)} · ${escapeHtml(data.pitchType)} · ${escapeHtml(data.matchDate)}
                                </div>
                            </div>
                            <div class="poster-scoreboard">
                                <div class="poster-team">
                                    ${logoASrc ? `<img src="${escapeHtml(logoASrc)}" class="poster-crest" alt="Team A" crossorigin="anonymous" />` : '<div class="poster-crest-fallback">⚽</div>'}
                                    <div class="poster-team-name">${escapeHtml(data.teamA.name)}</div>
                                </div>
                                <div class="poster-scoreline">
                                    <span class="poster-score-digit">${escapeHtml(data.scoreA)}</span>
                                    <span class="poster-score-sep">:</span>
                                    <span class="poster-score-digit">${escapeHtml(data.scoreB)}</span>
                                </div>
                                <div class="poster-team">
                                    ${logoBSrc ? `<img src="${escapeHtml(logoBSrc)}" class="poster-crest" alt="Team B" crossorigin="anonymous" />` : '<div class="poster-crest-fallback">🛡️</div>'}
                                    <div class="poster-team-name">${escapeHtml(data.teamB.name)}</div>
                                </div>
                            </div>
                            <div class="poster-scorers-box">
                                <div class="poster-scorers-col">${scorersAHtml}</div>
                                <div class="poster-scorers-col">${scorersBHtml}</div>
                            </div>
                            ${data.motm ? `
                                <div class="poster-motm-banner">
                                    <div class="poster-motm-badge">
                                        ${motmPhotoSrc ? `<img src="${escapeHtml(motmPhotoSrc)}" class="poster-motm-avatar" alt="MOTM" crossorigin="anonymous" />` : '⭐'}
                                    </div>
                                    <div class="poster-motm-info text-start">
                                        <h4>MOTM: ${escapeHtml(data.motm.playerName)}</h4>
                                        <p>${escapeHtml(data.motm.teamName)}${data.motm.highlight ? ' · ' + escapeHtml(data.motm.highlight) : ''}</p>
                                    </div>
                                </div>
                            ` : ''}
                            <div class="poster-footer">
                                Kết quả chính thức xác nhận tại <strong>${escapeHtml(data.brandTag)}</strong>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            shareBtn.onclick = () => exportAndShare('targetMatchPoster', `AloBall-Match-${data.scoreA}-${data.scoreB}.png`, `Kết quả trận đấu ${data.teamA.name} ${data.scoreA} - ${data.scoreB} ${data.teamB.name} | AloBall`);
        } catch (err) {
            bodyEl.innerHTML = `<div class="alert alert-danger my-4">${escapeHtml(err.message)}</div>`;
        }
    };

    // Aspect Toggle (Feed 4:5 vs Story 9:16)
    window.togglePosterAspect = function (mode) {
        const poster = document.getElementById('targetMatchPoster') || document.getElementById('socialMatchPoster');
        const btnFeed = document.getElementById('btnAspectFeed');
        const btnStory = document.getElementById('btnAspectStory');
        if (!poster) return;

        if (mode === 'story') {
            poster.classList.remove('ratio-feed');
            poster.classList.add('ratio-story');
            btnStory?.classList.add('active');
            btnFeed?.classList.remove('active');
        } else {
            poster.classList.remove('ratio-story');
            poster.classList.add('ratio-feed');
            btnFeed?.classList.add('active');
            btnStory?.classList.remove('active');
        }
    };

    // 3. Export & 1-Tap Share Flow (html-to-image at pixelRatio: 2)
    async function exportAndShare(elementId, fileName, shareTitle) {
        const target = document.getElementById(elementId);
        if (!target) return;

        const shareBtn = document.getElementById('aloballShareBtn');
        const originalText = shareBtn ? shareBtn.innerHTML : '';
        if (shareBtn) {
            shareBtn.disabled = true;
            shareBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span> Đang tạo ảnh HD...';
        }

        try {
            if (document.fonts && document.fonts.ready) {
                await document.fonts.ready;
            }
            const h2i = await ensureHtmlToImage();
            const blob = await h2i.toBlob(target, {
                pixelRatio: 2,
                cacheBust: true,
                skipFonts: false
            });

            if (!blob) throw new Error('Không thể tạo file ảnh');

            const file = new File([blob], fileName, { type: 'image/png' });
            const isInApp = /Zalo|FBAN|FBAV/i.test(navigator.userAgent);

            // Step 1: Zalo / FB in-app WebView -> long-press image preview
            if (isInApp) {
                const dataUrl = URL.createObjectURL(blob);
                const bodyEl = document.getElementById('aloballShareModalBody');
                if (bodyEl) {
                    bodyEl.innerHTML = `
                        <div class="alert alert-warning py-2 mb-2">
                            <i class="bi bi-info-circle-fill me-1"></i> Nhấn giữ vào ảnh để Lưu hoặc Gửi Zalo/Facebook
                        </div>
                        <img src="${dataUrl}" class="img-fluid rounded shadow" style="max-height: 70vh;" alt="Poster" />
                    `;
                } else {
                    const wrapper = target.parentElement;
                    if (wrapper) {
                        wrapper.innerHTML = `
                            <div class="alert alert-warning py-2 mb-2">Nhấn giữ vào ảnh để Lưu hoặc Gửi Zalo/Facebook</div>
                            <img src="${dataUrl}" class="img-fluid rounded shadow" style="max-height: 70vh;" alt="Poster" />
                        `;
                    }
                }
                showToast('Nhấn giữ vào ảnh để Lưu hoặc Gửi Zalo/Facebook');
                return;
            }

            // Step 2: Mobile / PWA Native Share (Web Share API Level 2)
            if (navigator.canShare && navigator.canShare({ files: [file] })) {
                try {
                    await navigator.share({
                        files: [file],
                        title: shareTitle,
                        text: `${shareTitle} - https://aloball.vercel.app`
                    });
                    showToast('Chia sẻ thành công!');
                    return;
                } catch (shareErr) {
                    if (shareErr.name === 'AbortError') return;
                }
            }

            // Step 3: Desktop Fallback (Clipboard + Download + exact Vietnamese toast)
            let copied = false;
            if (navigator.clipboard && window.ClipboardItem) {
                try {
                    await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
                    copied = true;
                } catch (_) { }
            }

            const downloadLink = document.createElement('a');
            downloadLink.href = URL.createObjectURL(blob);
            downloadLink.download = fileName;
            document.body.appendChild(downloadLink);
            downloadLink.click();
            document.body.removeChild(downloadLink);

            if (copied) {
                showToast('Đã sao chép ảnh! Dán (Ctrl+V) ngay vào Zalo hoặc Facebook');
            } else {
                showToast('Đã tải ảnh về thiết bị của bạn!');
            }
        } catch (err) {
            showToast('Lỗi khi xuất ảnh: ' + err.message, true);
        } finally {
            if (shareBtn) {
                shareBtn.disabled = false;
                shareBtn.innerHTML = originalText;
            }
        }
    }

    window.shareOrDownloadCard = exportAndShare;
})();
