window.mk = {
    get: k => { try { return localStorage.getItem(k); } catch { return null; } },
    set: (k, v) => { try { localStorage.setItem(k, v); } catch { } },
    back: () => history.back(),
    scrollToId: id => {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    },
    scrollTop: () => { const m = document.querySelector('main'); if (m) m.scrollTop = 0; }
};
