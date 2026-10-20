(() => {
  'use strict';
  const state={map:null,routeLayer:null,markerLayer:null,currentPathStyle:'theme-neon-blue'};
  const themeColors={'theme-neon-blue':['#1688ff','#8fd2ff'],'theme-neon-purple':['#8b5cf6','#c4b5fd'],'theme-solid-red':['#ef4444','#fca5a5'],'theme-solid-orange':['#f97316','#fdba74']};
  const $=id=>document.getElementById(id);
  const asPoint=p=>({lat:Number(p.lat??p.latitude),lng:Number(p.lng??p.longitude)});
  const toLatLng=p=>{const q=asPoint(p);return new TMap.LatLng(q.lat,q.lng)};
  const post=data=>window.chrome?.webview?.postMessage(JSON.stringify({version:1,...data}));
  let geometry={points:[],alternatives:[],active:0,start:null,end:null,mode:'navigation'}, revision=-1;
  let lastCar=null,bearing=0,reduceMotion=false,style='default',view3d=false;
  function svgData(svg) { return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`; }
  function endpointSvg(color, text) { return svgData(`<svg xmlns="http://www.w3.org/2000/svg" width="42" height="48"><filter id="s"><feDropShadow dx="0" dy="3" stdDeviation="3" flood-opacity=".3"/></filter><path filter="url(#s)" fill="${color}" stroke="white" stroke-width="3" d="M21 2C10 2 3 10 3 20c0 14 18 27 18 27s18-13 18-27C39 10 32 2 21 2z"/><circle cx="21" cy="20" r="10" fill="white" fill-opacity=".18"/><text x="21" y="25" text-anchor="middle" fill="white" font-size="13" font-family="Microsoft YaHei" font-weight="700">${text}</text></svg>`); }
  const carSvg = () => svgData(`<svg xmlns="http://www.w3.org/2000/svg" width="44" height="44" viewBox="0 0 44 44"><filter id="s"><feDropShadow dx="0" dy="3" stdDeviation="3" flood-opacity=".35"/></filter><g filter="url(#s)"><circle cx="22" cy="22" r="18" fill="white"/><path fill="${themeColors[state.currentPathStyle]?.[0] || '#1688ff'}" d="M22 5 34 35 22 29 10 35z"/></g></svg>`);

  function normalizeBearing(value) { return ((Number(value) || 0) % 360 + 360) % 360; }
  function carStyleId(direction) { return `car-${Math.round(normalizeBearing(direction) / 5) * 5 % 360}`; }
  function createMarkerStyles() {
    const styles = {
      start:new TMap.MarkerStyle({width:42,height:48,anchor:{x:21,y:46},src:endpointSvg('#20b46a','起')}),
      end:new TMap.MarkerStyle({width:42,height:48,anchor:{x:21,y:46},src:endpointSvg('#ef4f4b','终')}),
      selected:new TMap.MarkerStyle({width:30,height:36,anchor:{x:15,y:34},src:endpointSvg('#1677ff','·')})
    };
    for (let bearing = 0; bearing < 360; bearing += 5) {
      // JT/T 808 and geographic bearings increase clockwise from north. Tencent
      // MarkerStyle.rotate increases counter-clockwise, so invert the angle.
      styles[`car-${bearing}`] = new TMap.MarkerStyle({
        width:44,
        height:44,
        anchor:{x:22,y:22},
        faceTo:'map',
        rotate:(360-bearing)%360,
        src:carSvg()
      });
    }
    return styles;
  }

  function createRouteLayer() {
    state.routeLayer?.setMap(null);
    const colors = themeColors[state.currentPathStyle] || themeColors['theme-neon-blue'];
    state.routeLayer = new TMap.MultiPolyline({
      map: state.map,
      styles: {
        alternative: new TMap.PolylineStyle({color:'rgba(82,105,136,.48)',width:6,borderWidth:3,borderColor:'rgba(255,255,255,.8)',lineCap:'round'}),
        shadow: new TMap.PolylineStyle({color:'rgba(17,34,58,.22)',width:13,lineCap:'round'}),
        active: new TMap.PolylineStyle({color:colors[0],width:8,borderWidth:2,borderColor:'#fff',lineCap:'round'})
      },
      geometries: []
    });
  }

  function createMarkerLayer() {
    state.markerLayer?.setMap(null);
    state.markerLayer = new TMap.MultiMarker({
      map: state.map,
      styles: createMarkerStyles(),
      geometries: []
    });
  }


  function markers(){
    if(!state.markerLayer)return;
    const g=[];
    if(geometry.start)g.push({id:'start',styleId:'start',position:toLatLng(geometry.start)});
    if(geometry.end)g.push({id:'end',styleId:'end',position:toLatLng(geometry.end)});
    if(geometry.mode==='single'&&geometry.points.length)g.push({id:'selected',styleId:'selected',position:toLatLng(geometry.points[0])});
    if(lastCar)g.push({id:'car',styleId:carStyleId(bearing),position:toLatLng(lastCar)});
    state.markerLayer.setGeometries(g);
  }
  function routes(){
    if(!state.routeLayer)return;
    const g=[];
    geometry.alternatives.forEach((line,i)=>{
      if(i!==geometry.active&&line.length>1)g.push({id:'alternative-'+i,styleId:'alternative',paths:line.map(toLatLng)});
    });
    if(geometry.points.length>1){const paths=geometry.points.map(toLatLng);g.push({id:'shadow',styleId:'shadow',paths},{id:'route',styleId:'active',paths});}
    state.routeLayer.setGeometries(g);
  }
  function render(data){
    if(!state.map)return;
    if(data.points&&data.revision!==revision){geometry={...geometry,...data};revision=data.revision;routes();}
    if(themeColors[data.pathStyle]&&state.currentPathStyle!==data.pathStyle){state.currentPathStyle=data.pathStyle;createRouteLayer();createMarkerLayer();routes();}
    if(data.mapStyle!==style){
      const center=state.map.getCenter(),zoom=state.map.getZoom();style=data.mapStyle;
      try{state.routeLayer?.setMap(null);state.markerLayer?.setMap(null);state.map.destroy();$('map').replaceChildren();createMap(center,zoom);routes()}catch(_){post({type:'error'})}
    }
    if(view3d!==!!data.use3d){view3d=!!data.use3d;state.map.setPitch(view3d?50:0);state.map.setRotation(0)}
    lastCar=data.car||lastCar;bearing=Number(data.bearing)||0;markers();
    if(data.follow&&lastCar)state.map.setCenter(toLatLng(lastCar));
  }
  function fit(){
    const line=geometry.points;if(!state.map||!line.length)return;
    if(line.length===1){state.map.setCenter(toLatLng(line[0]));state.map.setZoom(16);return}
    let minLat=90,maxLat=-90,minLng=180,maxLng=-180;
    for(const raw of line){const p=asPoint(raw);minLat=Math.min(minLat,p.lat);maxLat=Math.max(maxLat,p.lat);minLng=Math.min(minLng,p.lng);maxLng=Math.max(maxLng,p.lng)}
    const rect=$('map').getBoundingClientRect(),padding=Math.max(12,Math.min(64,Math.min(rect.width,rect.height)*.1));
    state.map.fitBounds(new TMap.LatLngBounds(new TMap.LatLng(minLat,minLng),new TMap.LatLng(maxLat,maxLng)),{padding,ease:{duration:reduceMotion?0:220}});
  }
  window.ctsCanvas={
    render,fit,
    camera(data){if(!state.map||!data?.point)return;state.map.setCenter(toLatLng(data.point));if(data.zoom)state.map.setZoom(data.zoom)},
    appearance(data){reduceMotion=!!data.reduceMotion;document.documentElement.style.colorScheme=data.dark?'dark':'light'}
  };
  let dragged=false,ignoreUntil=0,pointer=null;
  $('map').addEventListener('pointerdown',e=>{pointer={id:e.pointerId,x:e.clientX,y:e.clientY}},true);
  $('map').addEventListener('pointermove',e=>{if(pointer?.id===e.pointerId&&Math.hypot(e.clientX-pointer.x,e.clientY-pointer.y)>8)ignoreUntil=performance.now()+350},true);
  window.addEventListener('pointerup',()=>{pointer=null},true);
  window.addEventListener('pointercancel',()=>{pointer=null},true);
  $('map').addEventListener('dragstart',e=>e.preventDefault());
  function createMap(center,zoom){
      state.map=new TMap.Map($('map'),{center,zoom,pitch:view3d?50:0,viewMode:'3D',baseMap:{type:'vector',features:['base','building3d','point','label']},...(style==='default'?{}:{mapStyleId:style})});
      createRouteLayer();createMarkerLayer();
      state.map.on('click',e=>{if(performance.now()>=ignoreUntil)post({type:'point',lat:e.latLng.getLat(),lng:e.latLng.getLng()})});
      state.map.on('dragstart',()=>{dragged=true;ignoreUntil=performance.now()+350});
      state.map.on('dragend',()=>{ignoreUntil=performance.now()+350});
      state.map.on('idle',()=>{const p=state.map.getCenter();post({type:'view',lat:p.getLat(),lng:p.getLng(),dragged});dragged=false});
  }
  window.__ctsTencentMapLoaded=()=>{
    try{
      createMap(new TMap.LatLng(39.9042,116.4074),11);
      post({type:'ready'});
    }catch(_){post({type:'error'})}
  };
  const key=window.__CTS_TENCENT_MAP_KEY;
  if(!key){post({type:'error'});return}
  // Report a capability failure separately from network and credential errors.
  // Keep the GL renderer as the sole map implementation on every platform.
  let gl=null;
  try{
    const probe=document.createElement('canvas');
    gl=probe.getContext('webgl2')||probe.getContext('webgl')||probe.getContext('experimental-webgl');
    gl?.getExtension('WEBGL_lose_context')?.loseContext();
  }catch(_){}
  if(!gl){post({type:'error',reason:'webgl-unavailable'});return}
  const script=document.createElement('script');script.charset='utf-8';script.src=`https://map.qq.com/api/gljs?v=1.exp&key=${encodeURIComponent(key)}&callback=__ctsTencentMapLoaded`;script.onerror=()=>post({type:'error'});document.head.appendChild(script);
  window.addEventListener('unhandledrejection',()=>post({type:'error'}));
  window.addEventListener('error',()=>post({type:'error'}));
})();
