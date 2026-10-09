(() => {
  const bridge = 'http://127.0.0.1:47831';
  let status = { enabled: false, baseUrl: '', allowedDomains: '', sessionToken: '' };
  let lastStatus = 0;

  function hostOf(value) {
    try { return new URL(value).hostname.toLowerCase(); } catch { return ''; }
  }

  function allowed() {
    if (!status.enabled) return false;
    const current = location.hostname.toLowerCase();
    const base = hostOf(status.baseUrl);
    if (base && current === base) return true;
    const extras = String(status.allowedDomains || '').split(/[,;\n\r]+/).map(x => hostOf(x.trim()) || x.trim().replace(/^\*?\.?/, '').replace(/\/$/, '').toLowerCase()).filter(Boolean);
    return extras.some(h => current === h || current.endsWith('.' + h));
  }

  async function refreshStatus(force = false) {
    if (!force && Date.now() - lastStatus < 5000) return status;
    lastStatus = Date.now();
    try {
      const r = await fetch(bridge + '/status', { cache: 'no-store' });
      if (r.ok) status = await r.json();
    } catch { status.enabled = false; }
    return status;
  }

  function cleanText(el) {
    if (!el) return '';
    const editable = el.matches?.('input,textarea,[contenteditable="true"]');
    if (editable) return '';
    const text = (el.innerText || el.textContent || '').replace(/\s+/g, ' ').trim();
    return text.slice(0, 140);
  }

  function stableId(id) {
    if (!id || id.length > 100) return false;
    const digits = (id.match(/\d/g) || []).length;
    if (digits > 8 && digits > id.length / 2) return false;
    return !/react-select-|ember/i.test(id);
  }

  function esc(value) {
    if (window.CSS && CSS.escape) return CSS.escape(value);
    return String(value).replace(/[^a-zA-Z0-9_-]/g, '\\$&');
  }

  function cssPath(el) {
    if (!(el instanceof Element)) return '';
    const testId = el.getAttribute('data-testid') || el.getAttribute('data-test') || el.getAttribute('data-qa');
    if (testId) return `[data-testid="${String(testId).replace(/"/g, '\\"')}"]`;
    if (stableId(el.id)) return `#${esc(el.id)}`;
    const parts = [];
    let node = el;
    while (node && node.nodeType === 1 && parts.length < 5) {
      let part = node.tagName.toLowerCase();
      const name = node.getAttribute('name');
      if (name) part += `[name="${String(name).replace(/"/g, '\\"')}"]`;
      else {
        const parent = node.parentElement;
        if (parent) {
          const peers = [...parent.children].filter(x => x.tagName === node.tagName);
          if (peers.length > 1) part += `:nth-of-type(${peers.indexOf(node) + 1})`;
        }
      }
      parts.unshift(part);
      if (node.parentElement && stableId(node.parentElement.id)) {
        parts.unshift(`#${esc(node.parentElement.id)}`);
        break;
      }
      node = node.parentElement;
    }
    return parts.join(' > ');
  }

  function xpath(el) {
    if (!(el instanceof Element)) return '';
    if (stableId(el.id)) return `//*[@id=${JSON.stringify(el.id)}]`;
    const segments = [];
    let node = el;
    while (node && node.nodeType === 1 && node !== document.documentElement) {
      let index = 1;
      let sib = node.previousElementSibling;
      while (sib) { if (sib.tagName === node.tagName) index++; sib = sib.previousElementSibling; }
      segments.unshift(`${node.tagName.toLowerCase()}[${index}]`);
      node = node.parentElement;
    }
    return '/html/' + segments.join('/');
  }

  function payload(type, el) {
    el = el instanceof Element ? el : document.activeElement;
    const testId = el?.getAttribute?.('data-testid') || el?.getAttribute?.('data-test') || el?.getAttribute?.('data-qa') || '';
    return {
      eventType: type,
      url: location.href,
      pageTitle: document.title || '',
      clientTimestamp: Date.now(),
      tagName: (el?.tagName || '').toLowerCase(),
      text: cleanText(el),
      id: stableId(el?.id || '') ? el.id : '',
      name: el?.getAttribute?.('name') || '',
      role: el?.getAttribute?.('role') || '',
      ariaLabel: el?.getAttribute?.('aria-label') || '',
      dataTestId: testId,
      cssSelector: cssPath(el),
      xpath: xpath(el),
      inputType: el?.getAttribute?.('type') || '',
      browserName: /Edg\//.test(navigator.userAgent) ? 'Edge' : /Chrome\//.test(navigator.userAgent) ? 'Chrome' : /Firefox\//.test(navigator.userAgent) ? 'Firefox' : 'Browser',
      frameUrl: location.href,
      isTopFrame: window.top === window
    };
  }

  async function send(type, el) {
    await refreshStatus();
    if (!allowed()) return;
    try {
      await fetch(bridge + '/event', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-QAFlow-Token': status.sessionToken || '' },
        body: JSON.stringify(payload(type, el)),
        keepalive: true
      });
    } catch { }
  }

  document.addEventListener('click', e => send('click', e.target), true);
  document.addEventListener('change', e => send(e.target?.matches?.('select') ? 'select' : 'input', e.target), true);
  document.addEventListener('input', e => {
    clearTimeout(e.target.__qaflowTimer);
    e.target.__qaflowTimer = setTimeout(() => send('input', e.target), 450);
  }, true);
  document.addEventListener('submit', e => send('submit', e.submitter || e.target), true);
  window.addEventListener('popstate', () => send('navigation', document.documentElement), true);
  window.addEventListener('hashchange', () => send('navigation', document.documentElement), true);
  window.addEventListener('pageshow', () => { refreshStatus(true); send('navigation', document.documentElement); }, true);
  refreshStatus(true);
})();
