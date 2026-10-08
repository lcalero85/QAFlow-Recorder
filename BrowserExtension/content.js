(() => {
  let recording = false, paused = false, inputTimer = null;
  const safeText = s => (s || '').replace(/\s+/g,' ').trim().slice(0,180);
  const cssEscape = v => (self.CSS && CSS.escape) ? CSS.escape(v) : String(v).replace(/[^a-zA-Z0-9_-]/g,'\\$&');
  const selector = el => {
    if (!(el instanceof Element)) return '';
    const test = el.getAttribute('data-testid'); if (test) return `[data-testid="${cssEscape(test)}"]`;
    if (el.id) return `#${cssEscape(el.id)}`;
    const name=el.getAttribute('name'); if(name) return `${el.tagName.toLowerCase()}[name="${cssEscape(name)}"]`;
    const aria=el.getAttribute('aria-label'); if(aria) return `${el.tagName.toLowerCase()}[aria-label="${aria.replace(/"/g,'\\"')}"]`;
    const parts=[]; let cur=el;
    while(cur && cur.nodeType===1 && parts.length<5){let p=cur.tagName.toLowerCase(); const parent=cur.parentElement;if(parent){const sib=[...parent.children].filter(x=>x.tagName===cur.tagName);if(sib.length>1)p+=`:nth-of-type(${sib.indexOf(cur)+1})`;}parts.unshift(p);cur=parent;}
    return parts.join(' > ');
  };
  const label = el => safeText(el.getAttribute?.('aria-label') || el.getAttribute?.('title') || el.innerText || el.value || el.getAttribute?.('placeholder') || el.name || el.id || el.tagName);
  const emit=(type,el,extra={})=>{ if(!recording||paused)return; chrome.runtime.sendMessage({type:'QAFLOW_EVENT',event:{eventType:type,timestamp:new Date().toISOString(),url:location.href,pageTitle:document.title,tagName:el?.tagName?.toLowerCase()||'',controlName:label(el),role:el?.getAttribute?.('role')||'',selector:selector(el),...extra}}).catch(()=>{}); };
  addEventListener('click',e=>emit('click',e.target),true);
  addEventListener('submit',e=>emit('submit',e.target),true);
  addEventListener('change',e=>emit(e.target?.tagName==='SELECT'?'select':'input',e.target),true);
  addEventListener('input',e=>{clearTimeout(inputTimer);const el=e.target;inputTimer=setTimeout(()=>emit('input',el,{masked:true}),550);},true);
  let lastUrl=location.href; setInterval(()=>{if(location.href!==lastUrl){lastUrl=location.href;emit('navigation',document.documentElement,{url:lastUrl});}},700);
  chrome.runtime.onMessage.addListener(msg=>{if(msg?.type==='QAFLOW_STATE'){recording=!!msg.recording;paused=!!msg.paused;}});
  chrome.storage.local.get(['qaflowActive']).then(x=>{const s=x.qaflowActive;recording=!!s?.recording;paused=!!s?.paused;});
})();
