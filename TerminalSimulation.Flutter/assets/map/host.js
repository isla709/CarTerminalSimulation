(() => {
  'use strict';
  const queue=[];
  let timer=null;
  function flush(){
    if(timer!==null){clearTimeout(timer);timer=null}
    if(window.top!==window)return;
    const channel=window.CtsMap;
    if(typeof channel!=='function'&&!channel?.postMessage){timer=setTimeout(flush,50);return}
    while(queue.length){const message=queue.shift();if(typeof channel==='function')channel(JSON.parse(message),()=>{});else channel.postMessage(message)}
  }
  window.chrome??={};
  window.chrome.webview={postMessage(message){
    if(typeof message!=='string'||message.length>8192)return;
    if(queue.length>=64)queue.shift();
    queue.push(JSON.stringify({token:window.__CTS_BRIDGE_TOKEN,message}));flush();
  }};
  document.addEventListener('click',event=>{const link=event.target.closest?.('a[href]');if(link&&!link.getAttribute('href').startsWith('#'))event.preventDefault()},true);
})();
