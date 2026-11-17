(() => {
    'use strict';
    const $ = (s, r = document) => r.querySelector(s);
    const $$ = (s, r = document) => [...r.querySelectorAll(s)];
    const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const conn = navigator.connection || {};
    const lite = conn.saveData || /(^|-)2g$/.test(conn.effectiveType || '');

    // Floodlight dust: light canvas particles, paused when off-screen or tab hidden
    const cv = $('#lpDust'), hero = $('.lp-hero');
    if (cv && !still) {
        const ctx = cv.getContext('2d');
        let w, h, run = true, dots = [];
        const fit = () => {
            const r = cv.getBoundingClientRect(), dpr = Math.min(devicePixelRatio || 1, 2);
            w = cv.width = r.width * dpr; h = cv.height = r.height * dpr;
            const n = r.width < 600 || lite ? 28 : 56;
            dots = Array.from({ length: n }, () => ({
                x: Math.random() * w, y: Math.random() * h, r: (Math.random() * 1.6 + .6) * dpr,
                vy: (Math.random() * .25 + .08) * dpr, vx: (Math.random() - .5) * .15 * dpr,
                gold: Math.random() < .25, p: Math.random() * 6.28
            }));
        };
        const tick = t => {
            if (run) {
                ctx.clearRect(0, 0, w, h);
                for (const d of dots) {
                    d.y -= d.vy; d.x += d.vx + Math.sin(t / 2000 + d.p) * .12;
                    if (d.y < -4) { d.y = h + 4; d.x = Math.random() * w; }
                    ctx.globalAlpha = .25 + .45 * Math.abs(Math.sin(t / 1500 + d.p));
                    ctx.fillStyle = d.gold ? '#FDE68A' : '#A7F3D0';
                    ctx.beginPath(); ctx.arc(d.x, d.y, d.r, 0, 6.28); ctx.fill();
                }
            }
            requestAnimationFrame(tick);
        };
        fit(); addEventListener('resize', fit);
        new IntersectionObserver(([e]) => run = e.isIntersecting && !document.hidden).observe(hero);
        document.addEventListener('visibilitychange', () => run = !document.hidden);
        requestAnimationFrame(tick);
    }

    // Background video: loads from src/data-src and auto-plays muted
    const vid = $('.lp-stage__video');
    if (vid && (vid.getAttribute('src') || vid.dataset.src) && !lite) {
        if (!vid.getAttribute('src')) vid.src = vid.dataset.src;
        vid.muted = true;
        const playVid = () => { vid.classList.add('is-ready'); vid.play().catch(() => {}); };
        if (vid.readyState >= 2) playVid();
        else vid.addEventListener('loadeddata', playVid, { once: true });
    }

    // Animated counters: start together when the strip enters the viewport
    const strip = $('.lp-tunnel'), stats = $$('[data-count-to]');
    if (strip && stats.length && !still) {
        const io = new IntersectionObserver(([e]) => {
            if (!e.isIntersecting) return;
            io.disconnect();
            const t0 = performance.now(), dur = 1400;
            const step = t => {
                const k = Math.min((t - t0) / dur, 1), eased = 1 - Math.pow(1 - k, 3);
                stats.forEach(el => el.textContent = Math.round(+el.dataset.from + (+el.dataset.countTo - +el.dataset.from) * eased));
                if (k < 1) requestAnimationFrame(step);
            };
            requestAnimationFrame(step);
        }, { threshold: .5 });
        io.observe(strip);
    }
    // Click feedback + subtle 3D tilt on glass cards
    const pulse = el => { el.classList.remove('is-pulse'); void el.offsetWidth; el.classList.add('is-pulse'); };
    $$('.lp-float').forEach(card => {
        card.addEventListener('click', e => { if (e.target.closest('button')) pulse(e.target.closest('button')); });
        if (still || !matchMedia('(hover:hover)').matches) return;
        card.addEventListener('pointermove', e => {
            const r = card.getBoundingClientRect();
            card.style.setProperty('--ry', ((e.clientX - r.left) / r.width - .5) * 10 + 'deg');
            card.style.setProperty('--rx', (.5 - (e.clientY - r.top) / r.height) * 8 + 'deg');
        });
        card.addEventListener('pointerleave', () => { card.style.removeProperty('--rx'); card.style.removeProperty('--ry'); });
    });

    // VietQR collect: each tap marks one more payer as paid, loops after 14/14
    const collect = $('[data-collect]');
    if (collect) {
        const n = $('b', collect), label = collect.firstChild;
        collect.addEventListener('click', () => {
            n.textContent = +n.textContent >= 14 ? 12 : +n.textContent + 1;
            label.textContent = n.textContent === '14' ? '● Đã thu đủ ' : '● Đã thu ';
        });
    }
})();