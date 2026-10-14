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
    currentMapStyle: 'map-style-default',
    currentPathStyle: 'theme-neon-blue',
    mapDefaultLocation: 'auto',
    requestSequence: 0,
    pendingSearchRequestId: 0,
    pendingSuggestionRequestId: 0,
    suggestions: [],
    searches: [],
    histories: [],
    historyKind: 'search',
    suggestionIndex: -1,
    searchComposing: false,
    suggestionCache: new Map(),
    mapGesture: null,
    ignoreClickUntil: 0,
    pendingRouteRequestId: 0
  };

  const $ = id => document.getElementById(id);
  const post = message => window.chrome?.webview?.postMessage(JSON.stringify(message));
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
  const toLatLng = point => L.latLng(Number(point.lat), Number(point.lng));
  const themeColors = {
    'theme-neon-blue': ['#00dff7', '#80f0ff'],
    'theme-neon-purple': ['#b026ff', '#e2acff'],
    'theme-solid-red': ['#4caf50', '#b8e1bb'],
    'theme-solid-orange': ['#2196f3', '#90caf9']
  };
  const carColors = {'theme-neon-blue':'#ff0055','theme-neon-purple':'#00ffcc','theme-solid-red':'#f44336','theme-solid-orange':'#ff9800'};
  const defaultCities = {
    beijing:[39.9042,116.4074],shanghai:[31.2304,121.4737],guangzhou:[23.1291,113.2644],shenzhen:[22.5431,114.0579],
    chengdu:[30.5728,104.0668],chongqing:[29.563,106.5516],hangzhou:[30.2741,120.1551],wuhan:[30.5928,114.3055],
    xian:[34.3416,108.9398],nanjing:[32.0603,118.7969],
    tianjin:[39.0842,117.2009],suzhou:[31.2989,120.5853],changsha:[28.2282,112.9388],zhengzhou:[34.7466,113.6254],qingdao:[36.0671,120.3826],xiamen:[24.4798,118.0894],jinan:[36.6512,117.1201],shenyang:[41.8057,123.4315],harbin:[45.8038,126.5349],kunming:[25.0389,102.7183],fuzhou:[26.0745,119.2965],hefei:[31.8206,117.2272],nanchang:[28.683,115.858],nanning:[22.817,108.367],guiyang:[26.647,106.63],urumqi:[43.826,87.617],lhasa:[29.652,91.172],hohhot:[40.8426,111.7492],haikou:[20.044,110.1999],lanzhou:[36.0611,103.8343],yinchuan:[38.4872,106.2309],xining:[36.6171,101.7782],taiyuan:[37.8706,112.549],shijiazhuang:[38.0428,114.515],changchun:[43.8171,125.324]
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
  const carSvg = () => svgData(`<svg xmlns="http://www.w3.org/2000/svg" width="44" height="44" viewBox="0 0 44 44"><filter id="s"><feDropShadow dx="0" dy="3" stdDeviation="3" flood-opacity=".35"/></filter><g filter="url(#s)"><circle cx="22" cy="22" r="18" fill="white"/><path fill="${carColors[state.currentPathStyle] || '#ff0055'}" d="M22 5 34 35 22 29 10 35z"/></g></svg>`);

  function normalizeBearing(value) { return ((Number(value) || 0) % 360 + 360) % 360; }
  function carStyleId(direction) { return `car-${Math.round(normalizeBearing(direction) / 5) * 5 % 360}`; }
  function carIcon(direction) {
    return L.divIcon({className:'route-car-icon',iconSize:[44,44],iconAnchor:[22,22],
      html:`<img src="${carSvg()}" width="44" height="44" draggable="false" style="transform:rotate(${normalizeBearing(direction)}deg)">`});
  }

  function createRouteLayer() {
    state.routeLayer?.remove();
    state.routeLayer=L.layerGroup().addTo(state.map);
  }

  function createMarkerLayer() {
    state.markerLayer?.remove();
    state.markerLayer=L.layerGroup().addTo(state.map);
    state.markers=new Map();
  }

  function syncMarkers() {
    if(!state.mapReady)return;
    const entries=[['start',state.routeStart],['end',state.routeEnd],['selected',state.selectedPoint],['car',state.renderedCarPoint]];
    for(const [id,point] of entries){
      const existing=state.markers.get(id);
      if(!point){if(existing){state.markerLayer.removeLayer(existing);state.markers.delete(id)}continue}
      const display=toLatLng(logicalToDisplay(point));
      if(existing){
        existing.setLatLng(display);
        if(id==='car'){const image=existing.getElement()?.querySelector('img');if(image)image.style.transform=`rotate(${normalizeBearing(state.carDirection)}deg)`}
        if(id==='start'||id==='end'){if(state.isSimulating)existing.dragging?.disable();else existing.dragging?.enable()}
        continue;
      }
      const src=id==='start'?endpointSvg('#20b46a','起'):id==='end'?endpointSvg('#ef4f4b','终'):endpointSvg('#1677ff','·');
      const image=L.icon({iconUrl:src,iconSize:id==='selected'?[30,36]:[42,48],iconAnchor:id==='selected'?[15,34]:[21,46]});
      const marker=L.marker(display,{icon:id==='car'?carIcon(state.carDirection):image,draggable:(id==='start'||id==='end')&&!state.isSimulating,interactive:id!=='car',zIndexOffset:id==='car'?1000:0}).addTo(state.markerLayer);
      if(id==='start'||id==='end')marker.on('dragend',event=>{
        if(state.isSimulating)return;
        const position=event.target.getLatLng(),logical=displayToLogical(position);
        setRoutePoint(id,logical,'地图选点');if(state.routeStart&&state.routeEnd)requestRoute();
      });
      state.markers.set(id,marker);
    }
  }

  function renderRoutes() {
    if(!state.mapReady)return;
    state.routeLayer.clearLayers();
    const colors=themeColors[state.currentPathStyle]||themeColors['theme-neon-blue'];
    const draw=(points,active)=>{
      if(points.length<2)return;
      const paths=points.map(point=>toLatLng(logicalToDisplay(point)));
      if(active)L.polyline(paths,{color:'#243b53',opacity:.16,weight:13,interactive:false}).addTo(state.routeLayer);
      const line=L.polyline(paths,{color:active?colors[0]:'#8da2b6',opacity:active?1:.6,weight:active?7:5,lineCap:'round',interactive:false}).addTo(state.routeLayer);
      if(active&&L.polylineDecorator)L.polylineDecorator(line,{patterns:[{offset:30,repeat:100,symbol:L.Symbol.arrowHead({pixelSize:10,pathOptions:{color:colors[0],fillColor:colors[0],fillOpacity:1,weight:0,interactive:false}})}]}).addTo(state.routeLayer);
    };
    state.routeResults.forEach((route,index)=>draw(route.logicalPoints,index===state.activeRouteIndex));
    if(!state.routeResults.length)draw(state.pathPoints,true);
  }

  function fitLogicalPoints(points) {
    if(!state.mapReady||!points.length)return;
    if(points.length===1){state.map.setView(toLatLng(logicalToDisplay(points[0])),16);return}
    const bounds=L.latLngBounds(points.map(point=>toLatLng(logicalToDisplay(point))));
    state.map.fitBounds(bounds,{paddingTopLeft:[Math.min(450,innerWidth/2),60],paddingBottomRight:[50,50],maxZoom:16});
  }

  function refreshRouteUi() {
    $('startPointName').textContent = state.routeStart?.name || '点击地图或搜索地点';
    $('endPointName').textContent = state.routeEnd?.name || '点击地图或搜索地点';
    $('planButton').disabled = !(state.routeStart && state.routeEnd) || state.isSimulating || !!state.pendingRouteRequestId;
    $('drawPointCount').textContent = `${state.pathPoints.length} 个点`;
    $('undoButton').disabled = !state.pathPoints.length || state.isSimulating;
    $('startButton').disabled = state.pathPoints.length < 2 || state.isSimulating;
    const single = state.routeMode === 'single';
    const recordable = single ? state.pathPoints.length===1 : state.pathPoints.length>=2;
    $('saveRouteButton').disabled = !recordable || state.isSimulating;
    $('recordRouteButton').disabled = !recordable || state.isSimulating;
    $('openRouteButton').disabled = state.isSimulating;
    $('startButton').classList.toggle('hidden', single || state.isSimulating);
    $('stopButton').classList.toggle('hidden', !state.isSimulating);
    $('clearButton').textContent = single ? '清除点位' : '清除路线';
    $('applyPointButton').disabled = !state.selectedPoint || state.isSimulating;
    $('singlePointName').textContent = state.selectedPoint?.name || '在地图上点击选择一个位置';
    $('singleCoordinates').textContent = state.selectedPoint
      ? `${coordinateLabel(state.coordinateSystem)} · ${state.selectedPoint.lat.toFixed(6)}, ${state.selectedPoint.lng.toFixed(6)}`
      : '再次选择会替换当前点，不生成路线';
    ['modeNavigation','modeDraw','modeSingle','clearStart','clearEnd','swapButton','clearButton','clearDrawButton','applyPointButton'].forEach(id=>{
      if(state.isSimulating) $(id).disabled=true;
      else if(id!=='applyPointButton') $(id).disabled=false;
    });
    if(state.isSimulating) return;
    if (state.routeMode === 'navigation') setStatus(state.routeStart ? (state.routeEnd ? '可以规划驾车路线' : '请选择终点') : '请选择起点');
    else if(single) setStatus(state.selectedPoint ? '单点已选择 · 可直接应用到位置上报' : '点击地图选择一个位置');
    else setStatus(state.pathPoints.length < 2 ? '在地图上依次点击绘制轨迹' : `自由路线已就绪 · ${state.pathPoints.length} 个点`);
  }

  function clearRouteGeometry() {
    state.pathPoints = [];
    state.routeResults = [];
    state.activeRouteIndex = 0;
    state.pendingRouteRequestId = 0;
    $('planButton').textContent='规划路线';
    state.carPoint = state.renderedCarPoint = null;
    cancelAnimationFrame(state.carAnimationFrame);
    renderRoutes(); syncMarkers(); renderAlternatives(); refreshRouteUi();
  }

  function setRouteMode(mode) {
    if (state.isSimulating || !['navigation','draw','single'].includes(mode)) return;
    if (mode !== state.routeMode) {
      clearRouteGeometry();
      state.routeStart = state.routeEnd = null;
      state.selectedPoint = null;
      syncMarkers();
    }
    state.routeMode = mode;
    $('modeNavigation').classList.toggle('active',mode==='navigation');
    $('modeDraw').classList.toggle('active',mode==='draw');
    $('modeSingle').classList.toggle('active',mode==='single');
    const modes=['navigation','draw','single'];
    $('modeTabs').style.setProperty('--mode-index',modes.indexOf(mode));
    ['modeNavigation','modeDraw','modeSingle'].forEach((id,index)=>$(id).setAttribute('aria-selected',String(index===modes.indexOf(mode))));
    $('navigationSection').classList.toggle('hidden',mode!=='navigation');
    $('drawSection').classList.toggle('hidden',mode!=='draw');
    $('singleSection').classList.toggle('hidden',mode!=='single');
    renderSearchResults();
    refreshRouteUi();
  }

  function setRoutePoint(role, point, name, poiId = '') {
    clearRouteGeometry();
    const value = {lat:Number(point.lat),lng:Number(point.lng),name:name||'地图选点',poiId};
    if (role === 'start') state.routeStart = value; else state.routeEnd = value;
    syncMarkers(); refreshRouteUi();
  }

  function handleMapClick(event) {
    if (state.isSimulating || performance.now() < state.ignoreClickUntil || state.mapGesture?.moved) return;
    closeSuggestions();
    const display = {lat:event.latlng.lat,lng:event.latlng.lng};
    const logical = displayToLogical(display);
    state.selectedPoint = logical;
    if(state.routeMode === 'single') {
      state.pathPoints=[logical];
      state.routeStart=state.routeEnd=null;
      renderRoutes();
    } else if (state.routeMode === 'draw') {
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
    if (query.length < 2 || query.length > 120 || !state.mapReady || state.pendingSearchRequestId) return;
    closeSuggestions();
    post({type:'map_history',action:'search',query});
    const center = state.map.getCenter();
    const requestId = ++state.requestSequence;
    state.pendingSearchRequestId = requestId;
    $('searchStatus').textContent = '正在搜索附近地点…';
    $('searchButton').disabled = true;
    post({type:'old_search',requestId,query,lat:center.lat,lng:center.lng});
  }

  function receiveSearch(payload) {
    if (Number(payload?.requestId) !== state.pendingSearchRequestId) return;
    state.pendingSearchRequestId = 0;
    $('searchButton').disabled = false;
    if (!payload?.succeeded) {
      $('searchStatus').textContent = '搜索失败';
      showToast(payload?.message || '地图搜索失败',true,payload?.errorCode===120?8000:4200);
      return;
    }
    state.searchResults = payload.results || [];
    $('searchStatus').textContent = state.searchResults.length ? `找到 ${state.searchResults.length} 个地点` : '没有找到匹配地点';
    renderSearchResults();
  }

  function renderSearchResults() {
    const box = $('searchResults');
    if (!state.searchResults.length) { box.style.display='none'; return; }
    box.innerHTML = state.searchResults.map((item,index)=>`<article class="search-result"><strong>${escapeHtml(item.title)}</strong><p>${escapeHtml([item.city,item.district,item.address].filter(Boolean).join(' · '))}<br>${escapeHtml(item.category)}</p><div class="result-actions">${state.routeMode==='single'?`<button data-role="single" data-index="${index}">选择此点</button>`:state.routeMode==='draw'?`<button data-role="draw" data-index="${index}">加入绘制</button>`:`<button data-role="start" data-index="${index}">设为起点</button><button data-role="end" data-index="${index}">设为终点</button>`}<button data-role="view" data-index="${index}">查看</button></div></article>`).join('');
    box.style.display='block';
  }

  function useSearchResult(index, role) {
    if(state.isSimulating && role!=='view') return showToast('请先停止模拟再修改路线');
    const item = state.searchResults[index]; if (!item) return;
    const logical = convertCoordinate(item.latitude,item.longitude,'wgs84',state.coordinateSystem);
    state.selectedPoint = logical;
    if (role === 'view') {
      state.map.panTo(toLatLng(logicalToDisplay(logical))); state.map.setZoom(16); syncMarkers(); return;
    }
    if(role==='single' || state.routeMode==='single') {
      state.selectedPoint={...logical,name:item.title};state.pathPoints=[{...state.selectedPoint}];
      state.routeStart=state.routeEnd=null;renderRoutes();syncMarkers();refreshRouteUi();applySelectedPoint();
      state.map.panTo(toLatLng(logicalToDisplay(logical)));state.map.setZoom(16);return;
    }
    if(role==='draw') {
      state.pathPoints.push({...logical,name:item.title});renderRoutes();syncMarkers();refreshRouteUi();
      state.map.panTo(toLatLng(logicalToDisplay(logical)));return;
    }
    setRouteMode('navigation');
    setRoutePoint(role,logical,item.title,item.id);
    state.map.panTo(toLatLng(logicalToDisplay(logical))); state.map.setZoom(15);
    if (state.routeStart && state.routeEnd) requestRoute();
  }

  function requestRoute() {
    if (!(state.routeStart && state.routeEnd) || state.isSimulating || state.pendingRouteRequestId) return;
    const start = logicalToDisplay(state.routeStart), end = logicalToDisplay(state.routeEnd);
    const requestId = ++state.requestSequence;
    state.pendingRouteRequestId = requestId;
    $('planButton').disabled = true; $('planButton').textContent='规划中…'; setStatus('地图正在计算实时驾车路线…');
    post({type:'old_route',requestId,start,end,startPoiId:state.routeStart.poiId||'',endPoiId:state.routeEnd.poiId||''});
  }

  function receiveRoutes(payload) {
    if (Number(payload?.requestId) !== state.pendingRouteRequestId) return;
    state.pendingRouteRequestId = 0;
    $('planButton').textContent='重新规划'; $('planButton').disabled=!(state.routeStart&&state.routeEnd);
    if (!payload?.succeeded) {
      const quotaMissing = payload?.errorCode === 120;
      showToast(payload?.message || '路线规划失败',true,quotaMissing?8000:4200);
      setStatus('路线规划失败 · 请检查网络连接');
      return;
    }
    state.routeResults = (payload.routes || []).map(route => ({...route,logicalPoints:(route.points||[]).map(point=>convertCoordinate(point.latitude,point.longitude,'wgs84',state.coordinateSystem))}));
    state.activeRouteIndex = 0;
    selectRoute(0,true);
  }

  function selectRoute(index, fit = false) {
    const route = state.routeResults[index]; if (!route) return;
    state.activeRouteIndex = index;
    state.pathPoints = route.logicalPoints.map(point=>({...point}));
    renderRoutes(); renderAlternatives(); refreshRouteUi();
    if (fit) fitLogicalPoints(state.pathPoints);
    setStatus(`${formatDistance(route.distance)} · 约 ${route.durationMinutes} 分钟`);
    post({type:'map_log',message:`地图已生成路线：${formatDistance(route.distance)}，约 ${route.durationMinutes} 分钟`});
  }

  function formatRouteTags(tags) {
    const labels={NARROW:'途经窄路',LEAST_LIGHT:'红绿灯少',LEAST_TIME:'时间短',LEAST_DISTANCE:'距离短',RECOMMEND:'推荐路线',HIGHWAY:'途经高速',AVOID_HIGHWAY:'不走高速',NO_TOLL:'不走收费路',TOLL:'收费道路'};
    return [...new Set((tags||[]).map(tag=>{
      const value=String(tag||'').trim();
      return labels[value.toUpperCase()] || (/[\u3400-\u9fff]/.test(value)&&!/[A-Za-z_]/.test(value)?value:'');
    }).filter(Boolean))].join(' · ');
  }
  function renderAlternatives() {
    const box=$('routeAlternatives');
    box.innerHTML=state.routeResults.map((route,index)=>`<button class="route-option ${index===state.activeRouteIndex?'active':''}" data-route="${index}"><b>${index===0?'推荐路线':`备选路线 ${index}`}</b><b>${route.durationMinutes} 分钟</b><span>${formatDistance(route.distance)}${route.toll?` · ¥${route.toll}`:''}</span><span>${escapeHtml(formatRouteTags(route.tags))}</span></button>`).join('');
  }

  function startSimulation() {
    if (state.pathPoints.length < 2) return;
    state.isSimulating=true;
    state.followVehicle=true;
    window.ctsVehicleUi.start(state.coordinateSystem);
    recordCurrentRoute(defaultRouteName(), true);
    $('startButton').classList.add('hidden'); $('stopButton').classList.remove('hidden');
    $('coordinateSystem').disabled=true; $('modeDraw').disabled=true; $('modeNavigation').disabled=true;
    post({type:'simulate_path',path:state.pathPoints,speed:Number($('speedInput').value)||60,coordinateSystem:state.coordinateSystem,vehicle:window.ctsVehicleUi.options});
    setStatus(`正在模拟 · ${coordinateLabel(state.coordinateSystem)} · ${$('speedInput').value || 60} km/h`);
    refreshRouteUi();
  }

  function stopSimulation(){post({type:'stop_simulation'});simulationFinished()}
  function simulationFinished(){window.ctsVehicleUi.finish();state.isSimulating=false;$('startButton').classList.remove('hidden');$('stopButton').classList.add('hidden');$('coordinateSystem').disabled=false;$('modeDraw').disabled=false;$('modeNavigation').disabled=false;refreshRouteUi()}

  function updateCarLocationLogical(lat,lng,direction){
    state.carPoint={lat:Number(lat),lng:Number(lng)};state.carDirection=Number(direction)||0;
    const from=state.renderedCarPoint||state.carPoint,to=state.carPoint,start=performance.now(),duration=120;
    cancelAnimationFrame(state.carAnimationFrame);
    const animate=now=>{const t=Math.min(1,(now-start)/duration),eased=1-Math.pow(1-t,3);state.renderedCarPoint={lat:from.lat+(to.lat-from.lat)*eased,lng:from.lng+(to.lng-from.lng)*eased};syncMarkers();if(state.followVehicle&&state.mapReady)state.map.panTo(toLatLng(logicalToDisplay(state.renderedCarPoint)));if(t<1)state.carAnimationFrame=requestAnimationFrame(animate)};
    state.carAnimationFrame=requestAnimationFrame(animate);
  }

  function requestCoordinateChange(){const next=$('coordinateSystem').value;if(next===state.coordinateSystem)return;$('coordinateSystem').value=state.coordinateSystem;state.pendingCoordinateSystem=next;$('coordinateFrom').textContent=coordinateLabel(state.coordinateSystem);$('coordinateTo').textContent=coordinateLabel(next);openOverlay('coordinateOverlay')}
  function cancelCoordinateChange(){state.pendingCoordinateSystem=null;$('coordinateSystem').value=state.coordinateSystem;closeOverlay('coordinateOverlay')}
  function applyCoordinateChange(convertExisting){const next=state.pendingCoordinateSystem;if(!next)return cancelCoordinateChange();const previous=state.coordinateSystem,convert=point=>{if(!point)return point;const value=convertCoordinate(point.lat,point.lng,previous,next);return{...point,...value}};if(convertExisting){state.pathPoints=state.pathPoints.map(convert);state.routeStart=convert(state.routeStart);state.routeEnd=convert(state.routeEnd);state.selectedPoint=convert(state.selectedPoint);state.carPoint=convert(state.carPoint);state.renderedCarPoint=convert(state.renderedCarPoint);state.routeResults.forEach(route=>route.logicalPoints=route.logicalPoints.map(convert))}state.coordinateSystem=next;cancelCoordinateChange();$('coordinateSystem').value=next;renderRoutes();syncMarkers();refreshRouteUi();showToast(`${convertExisting?'已转换坐标并保持地图位置':'已直接切换坐标解释'} · ${coordinateLabel(next)}`);post({type:'map_log',message:`路径、模拟及 JT/T 808 上报统一使用 ${coordinateLabel(next)} 坐标系`})}

  function routeDocument(){return{formatVersion:1,coordinateSystem:state.coordinateSystem,mode:state.routeMode,speed:Number($('speedInput').value)||60,vehicle:window.ctsVehicleUi.options,points:state.pathPoints.map(p=>({...p})),start:state.routeStart,end:state.routeEnd}}
  function saveRoute(){if(!state.pathPoints.length||state.isSimulating)return;post({type:'save_map_route',route:routeDocument()})}
  function openRoute(){post({type:'open_map_route'})}
  function applySelectedPoint(){
    if(!state.selectedPoint||state.isSimulating)return;
    const point=state.selectedPoint;
    post({type:'apply',lat:point.lat,lng:point.lng,coordinateSystem:state.coordinateSystem});
    showToast(`已应用 ${coordinateLabel(state.coordinateSystem)} 位置`);
  }

  const icon = name => `<svg class="icon" aria-hidden="true"><use href="#icon-${name}"/></svg>`;
  const overlayFocus = new Map();
  function openOverlay(id){
    closeSuggestions();overlayFocus.set(id,document.activeElement);
    $(id).classList.add('open');
    $(id).querySelector('input,button:not(:disabled)')?.focus();
  }
  function closeOverlay(id){$(id).classList.remove('open');const previous=overlayFocus.get(id);if(previous?.isConnected)previous.focus();else document.querySelector('.overlay.open input,.overlay.open button:not(:disabled)')?.focus();overlayFocus.delete(id)}
  function closeSuggestions(){
    clearTimeout(requestSuggestions.timer);state.pendingSuggestionRequestId=0;state.suggestionIndex=-1;
    $('searchSuggestions').classList.remove('open');$('searchInput').setAttribute('aria-expanded','false');$('searchInput').removeAttribute('aria-activedescendant');
  }
  function renderSuggestions(message=''){
    const box=$('searchSuggestions'),query=$('searchInput').value.trim();
    const history=!query;
    const items=history?state.searches.slice(0,8):state.suggestions;
    box.innerHTML=`<div class="suggestion-heading"><span>${history?'最近搜索':'地点联想'}</span><button class="quiet-button" data-manage-search>管理</button></div>`+
      items.map((item,index)=>`<div class="suggestion-row ${index===state.suggestionIndex?'active':''}" id="suggestion-${index}" role="option" aria-selected="${index===state.suggestionIndex}"><button class="suggestion-select" tabindex="-1" data-suggestion="${index}" data-history="${history}">${icon(history?'clock':'search')}<div><strong>${escapeHtml(history?item.query:item.title)}</strong>${history?'':`<small>${escapeHtml([item.city,item.district,item.address].filter(Boolean).join(' · '))}</small>`}</div></button>${history?`<button class="quiet-button" data-delete-search="${escapeHtml(item.id)}" aria-label="删除此搜索记录">${icon('close')}</button>`:''}</div>`).join('')+
      (!items.length?`<div class="suggestion-message">${escapeHtml(message||(history?'还没有搜索记录':'正在查找地点…'))}</div>`:'');
    box.classList.add('open');$('searchInput').setAttribute('aria-expanded','true');
    if(state.suggestionIndex>=0){$('searchInput').setAttribute('aria-activedescendant',`suggestion-${state.suggestionIndex}`);$(`suggestion-${state.suggestionIndex}`)?.scrollIntoView({block:'nearest'})}
    else $('searchInput').removeAttribute('aria-activedescendant');
  }
  function requestSuggestions(){
    clearTimeout(requestSuggestions.timer);state.pendingSuggestionRequestId=0;state.suggestionIndex=-1;state.suggestions=[];
    const query=$('searchInput').value.trim();
    if(state.searchComposing)return;
    if(!query){renderSuggestions();return}
    if(query.length<2){renderSuggestions('继续输入至少两个字符');return}
    if(query.length>120){renderSuggestions('搜索词不能超过 120 个字符');return}
    renderSuggestions(state.mapReady?'正在查找地点…':'地图加载后可使用地点联想');
    requestSuggestions.timer=setTimeout(()=>{
      if(!state.mapReady||document.activeElement!==$('searchInput'))return;
      const center=state.map.getCenter(),lat=center.lat,lng=center.lng;
      const cacheKey=`${query}|${lat.toFixed(2)}|${lng.toFixed(2)}`;
      if(state.suggestionCache.has(cacheKey)){state.suggestions=state.suggestionCache.get(cacheKey);renderSuggestions('没有联想地点，可点击搜索');return}
      const requestId=++state.requestSequence;state.pendingSuggestionRequestId=requestId;state.suggestionCacheKey=cacheKey;
      post({type:'old_suggest',requestId,query,lat,lng});
    },400);
  }
  function receiveSuggestions(payload){
    if(Number(payload?.requestId)!==state.pendingSuggestionRequestId||payload.query!==$('searchInput').value.trim())return;
    state.pendingSuggestionRequestId=0;
    state.suggestions=payload.succeeded?(payload.results||[]):[];
    if(payload.succeeded){
      if(state.suggestionCache.size>=30)state.suggestionCache.delete(state.suggestionCache.keys().next().value);
      state.suggestionCache.set(state.suggestionCacheKey,state.suggestions);
    }
    if(document.activeElement===$('searchInput'))renderSuggestions(payload.succeeded?'没有联想地点，可点击搜索':'联想暂不可用，仍可点击搜索');
  }
  function chooseSuggestion(index,history){
    if(history){const item=state.searches[index];if(!item)return;$('searchInput').value=item.query;closeSuggestions();requestSearch();return}
    const item=state.suggestions[index];if(!item)return;
    $('searchInput').value=item.title;state.searchResults=[item];closeSuggestions();renderSearchResults();
    $('searchStatus').textContent='已选择联想地点';post({type:'map_history',action:'search',query:item.title.slice(0,120)});
    useSearchResult(0,state.routeMode==='single'?'single':state.routeMode==='draw'?'draw':'view');
  }
  function bindSearchUi(){
    const input=$('searchInput');$('searchButton').onclick=requestSearch;
    input.addEventListener('focus',requestSuggestions);input.addEventListener('input',requestSuggestions);
    input.addEventListener('compositionstart',()=>{state.searchComposing=true;closeSuggestions()});
    input.addEventListener('compositionend',()=>{state.searchComposing=false;requestSuggestions()});
    input.addEventListener('keydown',event=>{
      if(event.isComposing||state.searchComposing)return;
      const open=$('searchSuggestions').classList.contains('open'),count=input.value.trim()?state.suggestions.length:Math.min(8,state.searches.length);
      if((event.key==='ArrowDown'||event.key==='ArrowUp')&&open&&count){event.preventDefault();state.suggestionIndex=(state.suggestionIndex+(event.key==='ArrowDown'?1:-1)+count)%count;renderSuggestions();return}
      if(event.key==='Escape'){event.preventDefault();closeSuggestions();return}
      if(event.key==='Enter'){event.preventDefault();if(open&&state.suggestionIndex>=0)chooseSuggestion(state.suggestionIndex,!input.value.trim());else requestSearch()}
    });
    $('searchSuggestions').addEventListener('mousedown',event=>event.preventDefault());
    $('searchSuggestions').onclick=event=>{
      const suggestion=event.target.closest('[data-suggestion]');
      if(suggestion)chooseSuggestion(Number(suggestion.dataset.suggestion),suggestion.dataset.history==='true');
      const remove=event.target.closest('[data-delete-search]');if(remove)post({type:'map_history',action:'delete',kind:'search',id:remove.dataset.deleteSearch});
      if(event.target.closest('[data-manage-search]'))showHistory('search');
    };
    document.addEventListener('pointerdown',event=>{if(!$('searchZone').contains(event.target))closeSuggestions()});
    input.addEventListener('blur',()=>setTimeout(()=>{if(!$('searchZone').contains(document.activeElement))closeSuggestions()},0));
  }
  function defaultRouteName(){return state.routeMode==='single'?(state.selectedPoint?.name||'单点位置'):state.routeMode==='navigation'?`${state.routeStart?.name||'起点'} → ${state.routeEnd?.name||'终点'}`:'自由绘制轨迹'}
  function recordCurrentRoute(name,automatic=false){
    if(!state.pathPoints.length)return;
    post({type:'map_history',action:'record',name:name.trim().slice(0,80),route:routeDocument(),automatic});
  }
  function receiveHistory(payload){
    $('saveRecord').disabled=false;
    if(!payload.succeeded){showToast(`记录操作失败：${payload.message||'请检查目录写入权限'}`,true);return}
    state.searches=payload.searches||[];state.histories=payload.routes||[];$('routeHistoryCount').textContent=state.histories.length;
    renderHistory();
    if(document.activeElement===$('searchInput')&&!$('searchInput').value.trim()&&$('searchSuggestions').classList.contains('open'))renderSuggestions();
    if(['record','rename'].includes(payload.action)&&!payload.automatic){closeOverlay('recordOverlay');showToast(payload.action==='rename'?'记录名称已更新':'轨迹已保存到记录库')}
  }
  function showHistory(kind){state.historyKind=kind;renderHistory();openOverlay('historyOverlay');post({type:'map_history',action:'list'})}
  function renderHistory(){
    const route=state.historyKind==='route',items=route?state.histories:state.searches;
    $('historySearchTab').classList.toggle('active',!route);$('historyRouteTab').classList.toggle('active',route);
    $('historySearchTab').setAttribute('aria-selected',String(!route));$('historyRouteTab').setAttribute('aria-selected',String(route));
    $('historySummary').textContent=`${items.length} 条${route?'轨迹':'搜索'}记录`;$('clearHistory').disabled=!items.length;
$('historyList').innerHTML=items.length?items.map(item=>`<article class="history-entry">${icon(route?'route':'clock')}<div class="history-copy"><strong title="${escapeHtml(route?item.name:item.query)}">${escapeHtml(route?item.name:item.query)}</strong><p>${route?`${coordinateLabel(item.coordinateSystem)} · ${{navigation:'导航',draw:'绘制',single:'单点'}[item.mode]||'轨迹'} · ${item.pointCount} 个点 · ${escapeHtml(item.speedDescription||'固定 '+item.speed+' km/h')}<br>`:''}${escapeHtml(new Date(item.createdAtUtc).toLocaleString('zh-CN',{hour12:false}))}</p></div><div class="history-actions"><button class="quiet-button" data-history-use="${escapeHtml(item.id)}" ${route&&state.isSimulating?'disabled':''}>${route?'恢复':'搜索'}</button>${route?`<button class="quiet-button" data-history-rename="${escapeHtml(item.id)}">重命名</button>`:''}<button class="quiet-button destructive" data-history-delete="${escapeHtml(item.id)}" aria-label="删除记录">${icon('trash')}</button></div></article>`).join(''):`<div class="empty-state">${icon(route?'route':'clock')}暂无${route?'轨迹':'搜索'}记录<br>${route?'开始模拟时自动记录，也可手动记入轨迹库':'搜索过的关键词将在此显示'}</div>`;
  }
  function askDelete(message,action){$('confirmMessage').textContent=message;askDelete.action=action;openOverlay('confirmOverlay')}
  function bindHistoryUi(){
    $('searchHistoryButton').onclick=()=>showHistory('search');$('routeHistoryButton').onclick=()=>showHistory('route');
    $('historySearchTab').onclick=()=>{state.historyKind='search';renderHistory()};$('historyRouteTab').onclick=()=>{state.historyKind='route';renderHistory()};
    $('doneHistory').onclick=$('closeHistory').onclick=()=>closeOverlay('historyOverlay');
    $('clearHistory').onclick=()=>askDelete(`清空全部${state.historyKind==='route'?'轨迹':'搜索'}记录？此操作不可撤销。`,()=>post({type:'map_history',action:'clear',kind:state.historyKind}));
    $('historyList').onclick=event=>{
      const use=event.target.closest('[data-history-use]');
      if(use){if(use.disabled)return;closeOverlay('historyOverlay');if(state.historyKind==='route')post({type:'map_history',action:'load',id:use.dataset.historyUse});else{const item=state.searches.find(x=>x.id===use.dataset.historyUse);if(item){$('searchInput').value=item.query;requestSearch()}}}
      const rename=event.target.closest('[data-history-rename]');
      if(rename){const item=state.histories.find(x=>x.id===rename.dataset.historyRename);if(!item)return;bindHistoryUi.renameId=item.id;$('recordTitle').textContent='重命名轨迹';$('recordName').value=item.name;openOverlay('recordOverlay');$('recordName').select()}
      const remove=event.target.closest('[data-history-delete]');
      if(remove){const kind=state.historyKind,id=remove.dataset.historyDelete;if(kind==='route')askDelete('删除这条轨迹记录？已导出的路线文件不受影响。',()=>post({type:'map_history',action:'delete',kind,id}));else post({type:'map_history',action:'delete',kind,id})}
    };
    $('recordRouteButton').onclick=()=>{bindHistoryUi.renameId=null;$('recordTitle').textContent='保存轨迹记录';$('recordName').value=defaultRouteName().slice(0,80);openOverlay('recordOverlay');$('recordName').select()};
    $('cancelRecord').onclick=()=>closeOverlay('recordOverlay');
    $('saveRecord').onclick=()=>{const name=$('recordName').value.trim();if(!name)return $('recordName').focus();$('saveRecord').disabled=true;if(bindHistoryUi.renameId)post({type:'map_history',action:'rename',id:bindHistoryUi.renameId,name});else recordCurrentRoute(name)};
    $('recordName').onkeydown=event=>{if(event.key==='Enter'&&!event.isComposing&&!$('saveRecord').disabled)$('saveRecord').click()};
    $('cancelConfirm').onclick=()=>{askDelete.action=null;closeOverlay('confirmOverlay')};$('acceptConfirm').onclick=()=>{const action=askDelete.action;askDelete.action=null;closeOverlay('confirmOverlay');action?.()};
    document.addEventListener('keydown',event=>{
      const overlays=Array.from(document.querySelectorAll('.overlay.open')).sort((a,b)=>Number(getComputedStyle(a).zIndex)-Number(getComputedStyle(b).zIndex));
      const top=overlays.at(-1);if(!top)return;
      if(event.key==='Escape'){event.preventDefault();event.stopPropagation();if(top.id==='coordinateOverlay')cancelCoordinateChange();else if(top.id==='vehicleOverlay')window.ctsVehicleUi.closeSettings();else closeOverlay(top.id);return}
      if(event.key==='Tab'){const controls=Array.from(top.querySelectorAll('button:not(:disabled),input:not(:disabled),select:not(:disabled)')).filter(x=>x.getClientRects().length);const first=controls[0],last=controls.at(-1);if(!top.contains(document.activeElement)){event.preventDefault();first?.focus()}else if(event.shiftKey&&document.activeElement===first){event.preventDefault();last?.focus()}else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first?.focus()}}
    });
    ['settingsOverlay','historyOverlay','recordOverlay','confirmOverlay'].forEach(id=>$(id).addEventListener('click',event=>{if(event.target===$(id))closeOverlay(id)}));
  }
  function bindMapGestures(){
    const map=$('map');map.draggable=false;
    map.addEventListener('dragstart',event=>{event.preventDefault();event.stopPropagation()},true);
    map.addEventListener('pointerdown',event=>{
      if(event.button!==0||event.target.closest('button,input,a,[role="button"]'))return;
      state.mapGesture={id:event.pointerId,x:event.clientX,y:event.clientY,moved:false};
    },true);
    window.addEventListener('pointermove',event=>{
      const gesture=state.mapGesture;if(!gesture||gesture.id!==event.pointerId)return;
      if(Math.hypot(event.clientX-gesture.x,event.clientY-gesture.y)>5){gesture.moved=true;state.followVehicle=false}
    },true);
    const finish=event=>{if(state.mapGesture?.id!==event.pointerId)return;if(state.mapGesture.moved)state.ignoreClickUntil=performance.now()+300;state.mapGesture=null};
    window.addEventListener('pointerup',finish,true);window.addEventListener('pointercancel',finish,true);
    window.addEventListener('blur',()=>{state.mapGesture=null});
  }
  function loadRouteFile(document){
    if(state.isSimulating)return showToast('请先停止模拟再打开轨迹',true);
    if(!document?.points?.length)return;
    clearRouteGeometry();state.selectedPoint=null;
    setRouteMode(['draw','single'].includes(document.mode)?document.mode:'navigation');
    state.coordinateSystem=(document.coordinateSystem||'gcj02').toLowerCase();$('coordinateSystem').value=state.coordinateSystem;
    state.pathPoints=document.points.map(point=>({lat:Number(point.lat),lng:Number(point.lng),name:point.name||''}));
    state.routeStart=document.start?{...document.start,lat:Number(document.start.lat),lng:Number(document.start.lng)}:null;
    state.routeEnd=document.end?{...document.end,lat:Number(document.end.lat),lng:Number(document.end.lng)}:null;
    if(state.routeMode==='single'){state.pathPoints=state.pathPoints.slice(0,1);state.selectedPoint={...state.pathPoints[0]};state.routeStart=state.routeEnd=null}
    $('speedInput').value=Number(document.speed)||60;
    window.ctsVehicleUi.setOptions(document.vehicle||{enabled:false});
    state.routeResults=[];renderRoutes();renderAlternatives();syncMarkers();refreshRouteUi();fitLogicalPoints(state.pathPoints);
    showToast(`已恢复 ${coordinateLabel(state.coordinateSystem)} 轨迹 · ${state.pathPoints.length} 个点`);
    post({type:'map_log',message:`打开轨迹后，路径、模拟及 JT/T 808 上报统一使用 ${coordinateLabel(state.coordinateSystem)} 坐标系`});
  }
  function routeFileOperationResult(operation,succeeded,message){showToast(message||'操作完成',!succeeded)}

  function initializeMapSettings(settings){
    state.mapDefaultLocation=settings.defaultLocation||'auto';
    state.currentMapStyle=settings.mapStyle||'map-style-default';
    if(!$('mapStyle').querySelector(`option[value="${CSS.escape(state.currentMapStyle)}"]`))state.currentMapStyle='map-style-default';
    state.currentPathStyle=settings.pathStyle||'theme-neon-blue';
    $('defaultLocation').value=state.mapDefaultLocation;$('mapStyle').value=state.currentMapStyle;$('pathStyle').value=state.currentPathStyle;
    applyVisualSettings();applyDefaultLocation();
  }

  function createMap(center,zoom){
    const map=L.map('map',{zoomControl:false,dragging:true,boxZoom:false}).setView(center,zoom);
    L.control.zoom({position:'topright'}).addTo(map);
    L.tileLayer('https://webrd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=8&x={x}&y={y}&z={z}',{subdomains:['1','2','3','4'],attribution:'© 高德地图 · 搜索数据 © OpenStreetMap contributors · Photon / OSRM'}).addTo(map);
    return map;
  }

  function applyVisualSettings(){
    if(!state.mapReady)return;
    $('map').className=state.currentMapStyle;
    createRouteLayer();createMarkerLayer();renderRoutes();syncMarkers();
  }

  function applyDefaultLocation(){if(!state.mapReady)return;if(state.mapDefaultLocation==='auto'){post({type:'request_network_location',showFailure:false});return}const city=defaultCities[state.mapDefaultLocation];if(city){const display=wgs84ToGcj02(city[0],city[1]);state.map.panTo(toLatLng(display));state.map.setZoom(11);if(!state.pathPoints.length&&!state.routeStart)setStatus(`默认城市 · ${$('defaultLocation').selectedOptions[0]?.textContent||'已设置'}`)}}
  function applyNetworkLocation(location){
    if(!location||!state.mapReady)return;
    const display=wgs84ToGcj02(Number(location.lat),Number(location.lng));
    state.map.panTo(toLatLng(display));state.map.setZoom(11);
    state.accuracyLayer?.remove();state.accuracyLayer=L.layerGroup().addTo(state.map);
    L.circle(toLatLng(display),{radius:Math.max(1000,Number(location.accuracy)||25000),color:'#2196f3',weight:1,opacity:.4,fillOpacity:.06,interactive:false}).addTo(state.accuracyLayer);
    L.circleMarker(toLatLng(display),{radius:7,color:'#fff',weight:3,fillColor:'#2196f3',fillOpacity:1,interactive:false}).addTo(state.accuracyLayer);
    setStatus(`当前城市 · ${location.label||'设备网络位置'}`);
  }
  function networkLocationFailed(message){$('locateButton').disabled=false;if(!state.pathPoints.length&&!state.routeStart)setStatus('自动定位暂不可用 · 可在地图设置中选择默认城市');if(message)showToast(`自动定位不可用：${message}`,true)}

  function bindUi(){
    $('collapseButton').onclick=()=>{const body=$('plannerBody');body.classList.toggle('collapsed');const open=!body.classList.contains('collapsed');$('collapseButton').setAttribute('aria-expanded',String(open));$('collapseButton').title=open?'收起面板':'展开面板';$('collapseButton').setAttribute('aria-label',$('collapseButton').title);body.inert=!open;closeSuggestions()};
    $('settingsButton').onclick=()=>openOverlay('settingsOverlay');$('closeSettings').onclick=$('cancelSettings').onclick=()=>closeOverlay('settingsOverlay');
    $('saveSettings').onclick=()=>{const previousStyle=state.currentMapStyle;state.mapDefaultLocation=$('defaultLocation').value;state.currentMapStyle=$('mapStyle').value;state.currentPathStyle=$('pathStyle').value;$('settingsOverlay').classList.remove('open');applyVisualSettings(previousStyle!==state.currentMapStyle);applyDefaultLocation();post({type:'save_map_settings',defaultLocation:state.mapDefaultLocation,mapStyle:state.currentMapStyle,pathStyle:state.currentPathStyle})};
    $('resetSettings').onclick=()=>{$('defaultLocation').value='auto';$('mapStyle').value='map-style-default';$('pathStyle').value='theme-neon-blue'};
    bindSearchUi();bindHistoryUi();bindMapGestures();
    window.ctsVehicleUi.init(post,showToast,stopSimulation);
    $('searchResults').onclick=e=>{const button=e.target.closest('button[data-index]');if(button)useSearchResult(Number(button.dataset.index),button.dataset.role)};
    $('modeNavigation').onclick=()=>setRouteMode('navigation');$('modeDraw').onclick=()=>setRouteMode('draw');
    $('modeSingle').onclick=()=>setRouteMode('single');$('applyPointButton').onclick=applySelectedPoint;
    $('clearStart').onclick=()=>{state.routeStart=null;clearRouteGeometry();syncMarkers();refreshRouteUi()};$('clearEnd').onclick=()=>{state.routeEnd=null;clearRouteGeometry();syncMarkers();refreshRouteUi()};
    $('swapButton').onclick=()=>{[state.routeStart,state.routeEnd]=[state.routeEnd,state.routeStart];syncMarkers();refreshRouteUi();if(state.routeStart&&state.routeEnd)requestRoute()};$('planButton').onclick=requestRoute;
    $('routeAlternatives').onclick=e=>{const button=e.target.closest('[data-route]');if(button)selectRoute(Number(button.dataset.route),true)};
    $('undoButton').onclick=()=>{state.pathPoints.pop();renderRoutes();refreshRouteUi()};$('clearDrawButton').onclick=clearRouteGeometry;$('clearButton').onclick=()=>{if(state.isSimulating)return;clearRouteGeometry();state.routeStart=state.routeEnd=state.selectedPoint=null;syncMarkers();refreshRouteUi()};
    $('startButton').onclick=startSimulation;$('stopButton').onclick=stopSimulation;$('speedInput').onchange=()=>{if(state.isSimulating)post({type:'simulate_speed',speed:Number($('speedInput').value)||60})};
    $('coordinateSystem').onchange=requestCoordinateChange;$('cancelCoordinate').onclick=cancelCoordinateChange;$('directCoordinate').onclick=()=>applyCoordinateChange(false);$('convertCoordinate').onclick=()=>applyCoordinateChange(true);
    $('openRouteButton').onclick=openRoute;$('saveRouteButton').onclick=saveRoute;
    $('locateButton').onclick=()=>{if(state.isSimulating&&state.carPoint){state.followVehicle=true;state.map.panTo(toLatLng(logicalToDisplay(state.carPoint)));showToast('已恢复跟随车辆')}else post({type:'request_network_location',showFailure:true})};
  }

  function setPerspective(){ /* Leaflet intentionally remains a 2D map. */ }

  function initializeLeafletMap(){
    try {
      const center=wgs84ToGcj02(35.8617,104.1954);
      state.map=createMap(toLatLng(center),4);
      state.map.on('click',handleMapClick);
      state.map.on('dragstart',()=>{state.followVehicle=false;state.ignoreClickUntil=performance.now()+300});
      state.map.on('dragend',()=>{state.ignoreClickUntil=performance.now()+300});
      state.mapReady=true;createRouteLayer();createMarkerLayer();
      post({type:'request_map_settings'});post({type:'map_history',action:'list'});
      post({type:'map_log',message:'Leaflet 地图与轨迹页面已加载'});setStatus('正在获取默认位置…');
    }catch(error){showToast('地图资源加载失败，请检查网络连接',true);post({type:'map_log',message:`旧地图初始化失败：${error?.message||'未知错误'}`})}
  }

  window.initializeMapSettings=initializeMapSettings;
  window.applyNetworkLocation=applyNetworkLocation;
  window.networkLocationFailed=networkLocationFailed;
  window.updateCarLocationLogical=updateCarLocationLogical;
  window.simulationFinished=simulationFinished;
  window.loadRouteFile=loadRouteFile;
  window.routeFileOperationResult=routeFileOperationResult;
  window.ctsMap={receiveSearch,receiveRoutes,receiveSuggestions,receiveHistory,receiveVehicleSettings:window.ctsVehicleUi.receiveSettings,simulationAccepted:payload=>{if(!payload.accepted){simulationFinished();showToast(payload.message||'无法开始模拟',true)}}};
  window.addEventListener('error',event=>post({type:'map_log',message:`地图脚本错误：${event.message || '未知错误'}`}));
  window.addEventListener('unhandledrejection',event=>post({type:'map_log',message:`地图异步错误：${event.reason?.message || event.reason || '未知错误'}`}));
  bindUi();initializeLeafletMap();
})();
