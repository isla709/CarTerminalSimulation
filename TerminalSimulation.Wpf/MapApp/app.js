(() => {
  'use strict';

  const state = {
    map: null,
    routeLayer: null,
    markerLayer: null,
    accuracyLayer: null,
    mapReady: false,
    routeMode: 'navigation',
    coordinateSystem: 'gcj02',
    pendingCoordinateSystem: null,
    pathPoints: [],
    routeStart: null,
    routeEnd: null,
    selectedPoint: null,
    searchResults: [],
    routeResults: [],
    activeRouteIndex: 0,
    isSimulating: false,
    followVehicle: true,
    carPoint: null,
    carDirection: 0,
    renderedCarPoint: null,
    carAnimationFrame: 0,
    currentMapStyle: 'default',
    currentPathStyle: 'theme-neon-blue',
    mapDefaultLocation: 'auto',
    requestSequence: 0,
    pendingSearchRequestId: 0,
    pendingRouteRequestId: 0
  };

  const $ = id => document.getElementById(id);
  const post = message => window.chrome?.webview?.postMessage(JSON.stringify(message));
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
  const toLatLng = point => new TMap.LatLng(Number(point.lat), Number(point.lng));
  const themeColors = {
    'theme-neon-blue': ['#1688ff', '#8fd2ff'],
    'theme-neon-purple': ['#8b5cf6', '#c4b5fd'],
    'theme-solid-red': ['#ef4444', '#fca5a5'],
    'theme-solid-orange': ['#f97316', '#fdba74']
  };
  const defaultCities = {
    beijing:[39.9042,116.4074],shanghai:[31.2304,121.4737],guangzhou:[23.1291,113.2644],shenzhen:[22.5431,114.0579],
    chengdu:[30.5728,104.0668],chongqing:[29.563,106.5516],hangzhou:[30.2741,120.1551],wuhan:[30.5928,114.3055],
    xian:[34.3416,108.9398],nanjing:[32.0603,118.7969]
  };

  function showToast(message, error = false, duration = 3200) {
    const toast = $('toast');
    toast.textContent = message;
    toast.classList.toggle('error', error);
    toast.classList.add('show');
    clearTimeout(showToast.timer);
    showToast.timer = setTimeout(() => toast.classList.remove('show'), duration);
  }

  function setStatus(message) { $('routeStatus').textContent = message; }
  function formatDistance(meters) { return meters >= 1000 ? `${(meters / 1000).toFixed(1)} km` : `${Math.round(meters)} m`; }
  function coordinateLabel(system) { return system === 'wgs84' ? 'WGS-84' : system === 'bd09' ? 'BD-09' : 'GCJ-02'; }

  function outOfChina(lat, lon) { return lon < 72.004 || lon > 137.8347 || lat < .8293 || lat > 55.8271; }
  function transformLat(x,y){let r=-100+2*x+3*y+.2*y*y+.1*x*y+.2*Math.sqrt(Math.abs(x));r+=(20*Math.sin(6*x*Math.PI)+20*Math.sin(2*x*Math.PI))*2/3;r+=(20*Math.sin(y*Math.PI)+40*Math.sin(y/3*Math.PI))*2/3;r+=(160*Math.sin(y/12*Math.PI)+320*Math.sin(y*Math.PI/30))*2/3;return r}
  function transformLon(x,y){let r=300+x+2*y+.1*x*x+.1*x*y+.1*Math.sqrt(Math.abs(x));r+=(20*Math.sin(6*x*Math.PI)+20*Math.sin(2*x*Math.PI))*2/3;r+=(20*Math.sin(x*Math.PI)+40*Math.sin(x/3*Math.PI))*2/3;r+=(150*Math.sin(x/12*Math.PI)+300*Math.sin(x/30*Math.PI))*2/3;return r}
  function wgs84ToGcj02(lat,lon){if(outOfChina(lat,lon))return{lat,lng:lon};const ee=.006693421622965943,a=6378245;let dLat=transformLat(lon-105,lat-35),dLon=transformLon(lon-105,lat-35),rad=lat/180*Math.PI,magic=1-ee*Math.sin(rad)**2,sqrt=Math.sqrt(magic);dLat=dLat*180/((a*(1-ee))/(magic*sqrt)*Math.PI);dLon=dLon*180/(a/sqrt*Math.cos(rad)*Math.PI);return{lat:lat+dLat,lng:lon+dLon}}
  function gcj02ToWgs84(lat,lon){if(outOfChina(lat,lon))return{lat,lng:lon};let guess={lat,lng:lon};for(let i=0;i<8;i++){const transformed=wgs84ToGcj02(guess.lat,guess.lng);guess={lat:guess.lat+(lat-transformed.lat),lng:guess.lng+(lon-transformed.lng)}}return guess}
  function gcj02ToBd09(lat,lon){const xPi=Math.PI*3000/180,z=Math.sqrt(lon*lon+lat*lat)+.00002*Math.sin(lat*xPi),theta=Math.atan2(lat,lon)+.000003*Math.cos(lon*xPi);return{lat:z*Math.sin(theta)+.006,lng:z*Math.cos(theta)+.0065}}
  function bd09ToGcj02(lat,lon){const x=lon-.0065,y=lat-.006,xPi=Math.PI*3000/180,z=Math.sqrt(x*x+y*y)-.00002*Math.sin(y*xPi),theta=Math.atan2(y,x)-.000003*Math.cos(x*xPi);return{lat:z*Math.sin(theta),lng:z*Math.cos(theta)}}
  function convertCoordinate(lat,lng,from,to){if(from===to)return{lat:Number(lat),lng:Number(lng)};let wgs;if(from==='wgs84')wgs={lat:Number(lat),lng:Number(lng)};else if(from==='gcj02')wgs=gcj02ToWgs84(Number(lat),Number(lng));else{const gcj=bd09ToGcj02(Number(lat),Number(lng));wgs=gcj02ToWgs84(gcj.lat,gcj.lng)}if(to==='wgs84')return wgs;const gcj=wgs84ToGcj02(wgs.lat,wgs.lng);return to==='gcj02'?gcj:gcj02ToBd09(gcj.lat,gcj.lng)}
  const logicalToDisplay = point => convertCoordinate(point.lat, point.lng, state.coordinateSystem, 'gcj02');
  const displayToLogical = point => convertCoordinate(point.lat, point.lng, 'gcj02', state.coordinateSystem);

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

  function syncMarkers() {
    if (!state.mapReady) return;
    const geometries = [];
    if (state.routeStart) geometries.push({id:'start',styleId:'start',position:toLatLng(logicalToDisplay(state.routeStart))});
    if (state.routeEnd) geometries.push({id:'end',styleId:'end',position:toLatLng(logicalToDisplay(state.routeEnd))});
    if (state.selectedPoint) geometries.push({id:'selected',styleId:'selected',position:toLatLng(logicalToDisplay(state.selectedPoint))});
    if (state.renderedCarPoint) geometries.push({id:'car',styleId:carStyleId(state.carDirection),position:toLatLng(logicalToDisplay(state.renderedCarPoint))});
    state.markerLayer.setGeometries(geometries);
  }

  function renderRoutes() {
    if (!state.mapReady) return;
    const geometries = [];
    state.routeResults.forEach((route,index) => {
      const paths = route.logicalPoints.map(point => toLatLng(logicalToDisplay(point)));
      if (paths.length < 2) return;
      if (index === state.activeRouteIndex) geometries.push({id:`shadow-${index}`,styleId:'shadow',paths});
      geometries.push({id:`route-${index}`,styleId:index === state.activeRouteIndex ? 'active' : 'alternative',paths,rank:index === state.activeRouteIndex ? 10 : 1});
    });
    if (!state.routeResults.length && state.pathPoints.length >= 2) {
      const paths = state.pathPoints.map(point => toLatLng(logicalToDisplay(point)));
      geometries.push({id:'draw-shadow',styleId:'shadow',paths},{id:'draw-route',styleId:'active',paths,rank:10});
    }
    state.routeLayer.setGeometries(geometries);
  }

  function fitLogicalPoints(points) {
    if (!state.mapReady || points.length < 2) return;
    const displayPoints = points.map(logicalToDisplay);
    const lats = displayPoints.map(p=>p.lat), lngs = displayPoints.map(p=>p.lng);
    const bounds = new TMap.LatLngBounds(new TMap.LatLng(Math.min(...lats),Math.min(...lngs)),new TMap.LatLng(Math.max(...lats),Math.max(...lngs)));
    state.map.fitBounds(bounds,{padding:90});
  }

  function refreshRouteUi() {
    $('startPointName').textContent = state.routeStart?.name || '点击地图或搜索地点';
    $('endPointName').textContent = state.routeEnd?.name || '点击地图或搜索地点';
    $('planButton').disabled = !(state.routeStart && state.routeEnd) || state.isSimulating;
    $('drawPointCount').textContent = `${state.pathPoints.length} 个点`;
    $('undoButton').disabled = !state.pathPoints.length || state.isSimulating;
    $('startButton').disabled = state.pathPoints.length < 2 || state.isSimulating;
    $('saveRouteButton').disabled = state.pathPoints.length < 2;
    if (state.routeMode === 'navigation') setStatus(state.routeStart ? (state.routeEnd ? '可以规划驾车路线' : '请选择终点') : '请选择起点');
    else setStatus(state.pathPoints.length < 2 ? '在地图上依次点击绘制轨迹' : `自由路线已就绪 · ${state.pathPoints.length} 个点`);
  }

  function clearRouteGeometry() {
    state.pathPoints = [];
    state.routeResults = [];
    state.activeRouteIndex = 0;
    state.carPoint = state.renderedCarPoint = null;
    cancelAnimationFrame(state.carAnimationFrame);
    renderRoutes(); syncMarkers(); renderAlternatives(); refreshRouteUi();
  }

  function setRouteMode(mode) {
    if (state.isSimulating || !['navigation','draw'].includes(mode)) return;
    if (mode !== state.routeMode) {
      clearRouteGeometry();
      state.routeStart = state.routeEnd = null;
      syncMarkers();
    }
    state.routeMode = mode;
    $('modeNavigation').classList.toggle('active',mode==='navigation');
    $('modeDraw').classList.toggle('active',mode==='draw');
    $('navigationSection').classList.toggle('hidden',mode!=='navigation');
    $('drawSection').classList.toggle('hidden',mode!=='draw');
    refreshRouteUi();
  }

  function setRoutePoint(role, point, name, poiId = '') {
    clearRouteGeometry();
    const value = {lat:Number(point.lat),lng:Number(point.lng),name:name||'地图选点',poiId};
    if (role === 'start') state.routeStart = value; else state.routeEnd = value;
    syncMarkers(); refreshRouteUi();
  }

  function handleMapClick(event) {
    if (state.isSimulating) return;
    const display = {lat:event.latLng.getLat(),lng:event.latLng.getLng()};
    const logical = displayToLogical(display);
    state.selectedPoint = logical;
    if (state.routeMode === 'draw') {
      state.pathPoints.push(logical);
      renderRoutes();
    } else if (!state.routeStart || state.routeEnd) {
      state.routeEnd = null;
      setRoutePoint('start',logical,'地图选点');
    } else {
      setRoutePoint('end',logical,'地图选点');
      requestRoute();
    }
    syncMarkers(); refreshRouteUi();
    post({type:'apply',lat:logical.lat,lng:logical.lng,coordinateSystem:state.coordinateSystem});
  }

  function requestSearch() {
    const query = $('searchInput').value.trim();
    if (query.length < 2 || !state.mapReady || state.pendingSearchRequestId) return;
    const center = state.map.getCenter();
    const requestId = ++state.requestSequence;
    state.pendingSearchRequestId = requestId;
    $('searchStatus').textContent = '正在搜索腾讯地图 POI…';
    $('searchButton').disabled = true;
    post({type:'tencent_search',requestId,query,lat:center.getLat(),lng:center.getLng()});
  }

  function receiveSearch(payload) {
    if (Number(payload?.requestId) !== state.pendingSearchRequestId) return;
    state.pendingSearchRequestId = 0;
    $('searchButton').disabled = false;
    if (!payload?.succeeded) {
      $('searchStatus').textContent = '搜索失败';
      showToast(payload?.message || '腾讯地图搜索失败',true,payload?.errorCode===120?8000:4200);
      return;
    }
    state.searchResults = payload.results || [];
    $('searchStatus').textContent = state.searchResults.length ? `找到 ${state.searchResults.length} 个地点` : '没有找到匹配地点';
    renderSearchResults();
  }

  function renderSearchResults() {
    const box = $('searchResults');
    if (!state.searchResults.length) { box.style.display='none'; return; }
    box.innerHTML = state.searchResults.map((item,index)=>`<article class="search-result"><strong>${escapeHtml(item.title)}</strong><p>${escapeHtml([item.city,item.district,item.address].filter(Boolean).join(' · '))}<br>${escapeHtml(item.category)}</p><div class="result-actions"><button data-role="start" data-index="${index}">设为起点</button><button data-role="end" data-index="${index}">设为终点</button><button data-role="view" data-index="${index}">查看</button></div></article>`).join('');
    box.style.display='block';
  }

  function useSearchResult(index, role) {
    const item = state.searchResults[index]; if (!item) return;
    const logical = convertCoordinate(item.latitude,item.longitude,'gcj02',state.coordinateSystem);
    state.selectedPoint = logical;
    if (role === 'view') {
      state.map.setCenter(new TMap.LatLng(item.latitude,item.longitude)); state.map.setZoom(16); syncMarkers(); return;
    }
    setRouteMode('navigation');
    setRoutePoint(role,logical,item.title,item.id);
    state.map.setCenter(new TMap.LatLng(item.latitude,item.longitude)); state.map.setZoom(15);
    if (state.routeStart && state.routeEnd) requestRoute();
  }

  function requestRoute() {
    if (!(state.routeStart && state.routeEnd) || state.isSimulating || state.pendingRouteRequestId) return;
    const start = logicalToDisplay(state.routeStart), end = logicalToDisplay(state.routeEnd);
    const requestId = ++state.requestSequence;
    state.pendingRouteRequestId = requestId;
    $('planButton').disabled = true; $('planButton').textContent='规划中…'; setStatus('腾讯地图正在计算实时驾车路线…');
    post({type:'tencent_route',requestId,start,end,startPoiId:state.routeStart.poiId||'',endPoiId:state.routeEnd.poiId||''});
  }

  function receiveRoutes(payload) {
    if (Number(payload?.requestId) !== state.pendingRouteRequestId) return;
    state.pendingRouteRequestId = 0;
    $('planButton').textContent='重新规划'; $('planButton').disabled=!(state.routeStart&&state.routeEnd);
    if (!payload?.succeeded) {
      const quotaMissing = payload?.errorCode === 120;
      showToast(payload?.message || '路线规划失败',true,quotaMissing?8000:4200);
      setStatus(quotaMissing ? 'WebService 配额未分配 · 请在腾讯控制台为此 Key 分配路线接口额度' : '路线规划失败');
      return;
    }
    state.routeResults = (payload.routes || []).map(route => ({...route,logicalPoints:(route.points||[]).map(point=>convertCoordinate(point.latitude,point.longitude,'gcj02',state.coordinateSystem))}));
    state.activeRouteIndex = 0;
    selectRoute(0,true);
  }

  function selectRoute(index, fit = false) {
    const route = state.routeResults[index]; if (!route) return;
    state.activeRouteIndex = index;
    state.pathPoints = route.logicalPoints.map(point=>({...point}));
    renderRoutes(); renderAlternatives(); refreshRouteUi();
    if (fit) fitLogicalPoints(state.pathPoints);
    setStatus(`${formatDistance(route.distance)} · 约 ${route.durationMinutes} 分钟 · ${route.trafficLightCount || 0} 个红绿灯`);
    post({type:'map_log',message:`腾讯地图已生成路线：${formatDistance(route.distance)}，约 ${route.durationMinutes} 分钟`});
  }

  function renderAlternatives() {
    const box=$('routeAlternatives');
    box.innerHTML=state.routeResults.map((route,index)=>`<button class="route-option ${index===state.activeRouteIndex?'active':''}" data-route="${index}"><b>${index===0?'推荐路线':`备选路线 ${index}`}</b><b>${route.durationMinutes} 分钟</b><span>${formatDistance(route.distance)} · ${route.trafficLightCount||0} 个红绿灯${route.toll?` · ¥${route.toll}`:''}</span><span>${escapeHtml((route.tags||[]).join(' · '))}</span></button>`).join('');
  }

  function startSimulation() {
    if (state.pathPoints.length < 2) return;
    state.isSimulating=true;
    $('startButton').classList.add('hidden'); $('stopButton').classList.remove('hidden');
    $('coordinateSystem').disabled=true; $('modeDraw').disabled=true; $('modeNavigation').disabled=true;
    post({type:'simulate_path',path:state.pathPoints,speed:Number($('speedInput').value)||60,coordinateSystem:state.coordinateSystem});
    setStatus(`正在模拟 · ${coordinateLabel(state.coordinateSystem)} · ${$('speedInput').value || 60} km/h`);
    refreshRouteUi();
  }

  function stopSimulation(){post({type:'stop_simulation'});simulationFinished()}
  function simulationFinished(){state.isSimulating=false;$('startButton').classList.remove('hidden');$('stopButton').classList.add('hidden');$('coordinateSystem').disabled=false;$('modeDraw').disabled=false;$('modeNavigation').disabled=false;refreshRouteUi()}

  function updateCarLocationLogical(lat,lng,direction){
    state.carPoint={lat:Number(lat),lng:Number(lng)};state.carDirection=Number(direction)||0;
    const from=state.renderedCarPoint||state.carPoint,to=state.carPoint,start=performance.now(),duration=120;
    cancelAnimationFrame(state.carAnimationFrame);
    const animate=now=>{const t=Math.min(1,(now-start)/duration),eased=1-Math.pow(1-t,3);state.renderedCarPoint={lat:from.lat+(to.lat-from.lat)*eased,lng:from.lng+(to.lng-from.lng)*eased};syncMarkers();if(state.followVehicle&&state.mapReady)state.map.setCenter(toLatLng(logicalToDisplay(state.renderedCarPoint)));if(t<1)state.carAnimationFrame=requestAnimationFrame(animate)};
    state.carAnimationFrame=requestAnimationFrame(animate);
  }

  function requestCoordinateChange(){const next=$('coordinateSystem').value;if(next===state.coordinateSystem)return;$('coordinateSystem').value=state.coordinateSystem;state.pendingCoordinateSystem=next;$('coordinateFrom').textContent=coordinateLabel(state.coordinateSystem);$('coordinateTo').textContent=coordinateLabel(next);$('coordinateOverlay').classList.add('open')}
  function cancelCoordinateChange(){state.pendingCoordinateSystem=null;$('coordinateSystem').value=state.coordinateSystem;$('coordinateOverlay').classList.remove('open')}
  function applyCoordinateChange(convertExisting){const next=state.pendingCoordinateSystem;if(!next)return cancelCoordinateChange();const previous=state.coordinateSystem,convert=point=>{if(!point)return point;const value=convertCoordinate(point.lat,point.lng,previous,next);return{...point,...value}};if(convertExisting){state.pathPoints=state.pathPoints.map(convert);state.routeStart=convert(state.routeStart);state.routeEnd=convert(state.routeEnd);state.selectedPoint=convert(state.selectedPoint);state.carPoint=convert(state.carPoint);state.renderedCarPoint=convert(state.renderedCarPoint);state.routeResults.forEach(route=>route.logicalPoints=route.logicalPoints.map(convert))}state.coordinateSystem=next;cancelCoordinateChange();$('coordinateSystem').value=next;renderRoutes();syncMarkers();refreshRouteUi();showToast(`${convertExisting?'已转换坐标并保持地图位置':'已直接切换坐标解释'} · ${coordinateLabel(next)}`);post({type:'map_log',message:`路径、模拟及 JT/T 808 上报统一使用 ${coordinateLabel(next)} 坐标系`})}

  function saveRoute(){if(state.pathPoints.length<2)return;post({type:'save_map_route',route:{format:'CTSROUTE',version:1,applicationVersion:'',coordinateSystem:state.coordinateSystem,mode:state.routeMode,points:state.pathPoints,start:state.routeStart,end:state.routeEnd}})}
  function openRoute(){post({type:'open_map_route'})}
  function loadRouteFile(document){if(!document?.points?.length)return;state.coordinateSystem=(document.coordinateSystem||'gcj02').toLowerCase();$('coordinateSystem').value=state.coordinateSystem;state.routeMode=document.mode==='draw'?'draw':'navigation';setRouteMode(state.routeMode);state.pathPoints=document.points.map(point=>({lat:Number(point.lat),lng:Number(point.lng),name:point.name||''}));state.routeStart=document.start?{lat:Number(document.start.lat),lng:Number(document.start.lng),name:document.start.name||'起点'}:null;state.routeEnd=document.end?{lat:Number(document.end.lat),lng:Number(document.end.lng),name:document.end.name||'终点'}:null;state.routeResults=[];renderRoutes();syncMarkers();refreshRouteUi();fitLogicalPoints(state.pathPoints);showToast(`已打开 ${coordinateLabel(state.coordinateSystem)} 路线 · ${state.pathPoints.length} 个点`)}
  function routeFileOperationResult(operation,succeeded,message){showToast(message||'操作完成',!succeeded)}

  function initializeMapSettings(settings){state.mapDefaultLocation=settings.defaultLocation||'auto';state.currentMapStyle=settings.mapStyle||'default';if(!$('mapStyle').querySelector(`option[value="${CSS.escape(state.currentMapStyle)}"]`))state.currentMapStyle='default';state.currentPathStyle=settings.pathStyle||'theme-neon-blue';$('defaultLocation').value=state.mapDefaultLocation;$('mapStyle').value=state.currentMapStyle;$('pathStyle').value=state.currentPathStyle;applyVisualSettings(true);applyDefaultLocation()}
  function createMap(center,zoom,pitch=0,rotation=0){const options={center,zoom,pitch,rotation,viewMode:'3D',baseMap:{type:'vector',features:['base','building3d','point','label']}};if(state.currentMapStyle!=='default')options.mapStyleId=state.currentMapStyle;return new TMap.Map($('map'),options)}
  function applyVisualSettings(rebuildStyle=false){
    if(rebuildStyle&&state.mapReady){
      const center=state.map.getCenter(),zoom=state.map.getZoom(),pitch=state.map.getPitch(),rotation=typeof state.map.getRotation==='function'?state.map.getRotation():0;
      state.routeLayer?.setMap(null);state.markerLayer?.setMap(null);state.map.destroy?.();$('map').replaceChildren();
      try {
        state.map=createMap(center,zoom,pitch,rotation);
      } catch(error) {
        state.currentMapStyle='default';$('mapStyle').value='default';$('map').replaceChildren();
        state.map=createMap(center,zoom,pitch,rotation);
        showToast(`所选腾讯模板未对当前 Key 开放，已恢复默认地图：${error?.message||'样式不可用'}`,true,6000);
      }
      state.map.on('click',handleMapClick);
    }
    createRouteLayer();createMarkerLayer();renderRoutes();syncMarkers()
  }
  function applyDefaultLocation(){if(!state.mapReady)return;if(state.mapDefaultLocation==='auto'){post({type:'request_network_location',showFailure:false});return}const city=defaultCities[state.mapDefaultLocation];if(city){const display=wgs84ToGcj02(city[0],city[1]);state.map.setCenter(toLatLng(display));state.map.setZoom(11)}}
  function applyNetworkLocation(location){if(!location||!state.mapReady)return;const display=wgs84ToGcj02(Number(location.lat),Number(location.lng));state.map.setCenter(toLatLng(display));state.map.setZoom(11);setStatus(`当前城市 · ${location.label||'腾讯地图 IP 定位'}`)}
  function networkLocationFailed(message){$('locateButton').disabled=false;if(message)showToast(`自动定位不可用：${message}`,true)}

  function bindUi(){
    $('collapseButton').onclick=()=>{const body=$('plannerBody');body.classList.toggle('collapsed');$('collapseButton').textContent=body.classList.contains('collapsed')?'+':'−'};
    $('settingsButton').onclick=()=>{$('settingsOverlay').classList.add('open')};$('closeSettings').onclick=$('cancelSettings').onclick=()=>$('settingsOverlay').classList.remove('open');
    $('saveSettings').onclick=()=>{const previousStyle=state.currentMapStyle;state.mapDefaultLocation=$('defaultLocation').value;state.currentMapStyle=$('mapStyle').value;state.currentPathStyle=$('pathStyle').value;$('settingsOverlay').classList.remove('open');applyVisualSettings(previousStyle!==state.currentMapStyle);applyDefaultLocation();post({type:'save_map_settings',defaultLocation:state.mapDefaultLocation,mapStyle:state.currentMapStyle,pathStyle:state.currentPathStyle})};
    $('resetSettings').onclick=()=>{$('defaultLocation').value='auto';$('mapStyle').value='default';$('pathStyle').value='theme-neon-blue'};
    $('searchButton').onclick=requestSearch;$('searchInput').onkeydown=e=>{if(e.key==='Enter')requestSearch()};
    $('searchResults').onclick=e=>{const button=e.target.closest('button[data-index]');if(button)useSearchResult(Number(button.dataset.index),button.dataset.role)};
    $('modeNavigation').onclick=()=>setRouteMode('navigation');$('modeDraw').onclick=()=>setRouteMode('draw');
    $('clearStart').onclick=()=>{state.routeStart=null;clearRouteGeometry();syncMarkers();refreshRouteUi()};$('clearEnd').onclick=()=>{state.routeEnd=null;clearRouteGeometry();syncMarkers();refreshRouteUi()};
    $('swapButton').onclick=()=>{[state.routeStart,state.routeEnd]=[state.routeEnd,state.routeStart];syncMarkers();refreshRouteUi();if(state.routeStart&&state.routeEnd)requestRoute()};$('planButton').onclick=requestRoute;
    $('routeAlternatives').onclick=e=>{const button=e.target.closest('[data-route]');if(button)selectRoute(Number(button.dataset.route),true)};
    $('undoButton').onclick=()=>{state.pathPoints.pop();renderRoutes();refreshRouteUi()};$('clearDrawButton').onclick=clearRouteGeometry;$('clearButton').onclick=()=>{clearRouteGeometry();state.routeStart=state.routeEnd=state.selectedPoint=null;syncMarkers();refreshRouteUi()};
    $('startButton').onclick=startSimulation;$('stopButton').onclick=stopSimulation;$('speedInput').onchange=()=>{if(state.isSimulating)post({type:'simulate_speed',speed:Number($('speedInput').value)||60})};
    $('coordinateSystem').onchange=requestCoordinateChange;$('cancelCoordinate').onclick=cancelCoordinateChange;$('directCoordinate').onclick=()=>applyCoordinateChange(false);$('convertCoordinate').onclick=()=>applyCoordinateChange(true);
    $('openRouteButton').onclick=openRoute;$('saveRouteButton').onclick=saveRoute;
    $('locateButton').onclick=()=>post({type:'request_network_location',showFailure:true});
    $('view2dButton').onclick=()=>setPerspective(false);
    $('view3dButton').onclick=()=>setPerspective(true);
  }

  function setPerspective(use3d) {
    if (!state.mapReady) return;
    try {
      state.map.setPitch(use3d ? 50 : 0);
      state.map.setRotation(0);
      $('view2dButton').classList.toggle('active',!use3d);
      $('view3dButton').classList.toggle('active',use3d);
      setStatus(use3d ? '三维视角 · 放大地图可查看建筑细节' : '二维俯视 · 地图保持正北朝上');
    } catch (error) {
      showToast(`视角切换失败：${error?.message || '当前地图不支持该视角'}`,true);
      post({type:'map_log',message:`腾讯地图视角切换失败：${error?.message || '未知错误'}`});
    }
  }

  function initializeTencentMap(){
    try {
      const center=wgs84ToGcj02(35.8617,104.1954);
      state.map=createMap(toLatLng(center),4);
      state.map.on('click',handleMapClick);state.mapReady=true;createRouteLayer();createMarkerLayer();post({type:'request_map_settings'});post({type:'map_log',message:'腾讯地图 JavaScript API GL 已加载'});setStatus('正在获取默认位置…');
    } catch (error) {
      showToast('腾讯地图初始化失败，请检查 Key 来源限制',true);
      post({type:'map_log',message:`腾讯地图前端初始化失败：${error?.message || '未知错误'}`});
    }
  }

  function loadTencentApi(){
    const key=window.__CTS_TENCENT_MAP_KEY;
    if(!key){showToast('腾讯地图 Key 未配置',true);return}
    window.__ctsTencentMapLoaded=initializeTencentMap;
    const script=document.createElement('script');script.charset='utf-8';script.src=`https://map.qq.com/api/gljs?v=1.exp&key=${encodeURIComponent(key)}&callback=__ctsTencentMapLoaded`;script.onerror=()=>showToast('腾讯地图资源加载失败，请检查网络或 Key 配置',true);document.head.appendChild(script);
  }

  window.initializeMapSettings=initializeMapSettings;
  window.applyNetworkLocation=applyNetworkLocation;
  window.networkLocationFailed=networkLocationFailed;
  window.updateCarLocationLogical=updateCarLocationLogical;
  window.simulationFinished=simulationFinished;
  window.loadRouteFile=loadRouteFile;
  window.routeFileOperationResult=routeFileOperationResult;
  window.ctsMap={receiveSearch,receiveRoutes};
  window.addEventListener('error',event=>post({type:'map_log',message:`地图脚本错误：${event.message || '未知错误'}`}));
  window.addEventListener('unhandledrejection',event=>post({type:'map_log',message:`地图异步错误：${event.reason?.message || event.reason || '未知错误'}`}));
  bindUi();loadTencentApi();
})();
