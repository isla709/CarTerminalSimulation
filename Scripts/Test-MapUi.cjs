// Deterministic UI regression checks: no real map key or network quota is used.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require(path.join(process.env.CTS_NODE_MODULES ||
  path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'), 'playwright'));
const root = path.resolve(__dirname,'..');
const provider=process.argv[2]||'new';assert.ok(['new','old'].includes(provider));
const assets=provider==='old'?'MapAppOld':'MapApp';
const output = path.join(root,'artifacts','.qa',`map-ui-${provider}-${Date.now()}`);
fs.mkdirSync(output,{recursive:true});
const server = http.createServer((req,res)=>{
  const name = {'/':'index.html','/index.html':'index.html','/app.css':'app.css','/app.js':'app.js','/vehicle.js':'vehicle.js','/vehicle.css':'vehicle.css','/vendor/leaflet.js':'vendor/leaflet.js','/vendor/leaflet.css':'vendor/leaflet.css','/vendor/leaflet.polylineDecorator.js':'vendor/leaflet.polylineDecorator.js'}[req.url];
  if(!name){res.writeHead(404);return res.end()}
  res.setHeader('Content-Type', name.endsWith('.css')?'text/css':name.endsWith('.js')?'text/javascript':'text/html');
  const contents=fs.readFileSync(path.join(root,'TerminalSimulation.Wpf',assets,name));
  res.end(provider==='old'&&name==='app.js'?"L.Map.addInitHook(function(){window.__testMap=this});\n"+contents:contents);
});
const fakeSdk = `
(() => {
  class LatLng {constructor(lat,lng){this.lat=lat;this.lng=lng}getLat(){return this.lat}getLng(){return this.lng}}
  class Layer {constructor(options){this.geometries=[];this.options=options}setMap(){}setGeometries(items){this.geometries=items}}
  class Map {constructor(element,options){this.center=options.center;this.zoom=options.zoom;this.pitch=options.pitch;this.rotation=0;this.events={};window.__testMap=this;element.style.background='linear-gradient(135deg,#e1eaf0,#f3f6f8)';element.style.cursor='grab'}on(name,handler){this.events[name]=handler}getCenter(){return this.center}setCenter(value){this.center=value}getZoom(){return this.zoom}setZoom(value){this.zoom=value}getPitch(){return this.pitch}setPitch(value){this.pitch=value}getRotation(){return this.rotation}setRotation(value){this.rotation=value}fitBounds(){}destroy(){}}
  window.TMap={LatLng,Map,LatLngBounds:class{},MultiPolyline:Layer,MultiMarker:class extends Layer{constructor(options){super(options);window.__testMarkers=this}},MarkerStyle:class{constructor(options){Object.assign(this,options)}},PolylineStyle:class{}};
  window.__ctsTencentMapLoaded();
})();`;

(async()=>{
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const edge='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
  const browser=await chromium.launch({headless:true,...(fs.existsSync(edge)?{executablePath:edge}:{})});
  const page=await browser.newPage({viewport:{width:1280,height:800}});
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.route('https://webrd*/**',route=>route.abort());
  await page.route('https://map.qq.com/api/gljs*',route=>route.fulfill({contentType:'text/javascript',body:fakeSdk}));
  await page.addInitScript(()=>{
    window.__CTS_TENCENT_MAP_KEY='mock-key-for-offline-test';
    window.__messages=[];window.__history={searches:[],routes:[]};
    const defaults={enabled:false,preset:'small-auto',maxSpeedKph:100,averageSpeedKph:50,drivingStyle:'normal',maxAcceleration:2.6,brakeDeceleration:6,transmission:'automatic',powertrain:'petrol',gearRatios:[3.4,2.1,1.4,1,.8,.64],finalDrive:4.1,wheelRadius:.3,idleRpm:800,redlineRpm:6500,upshiftRpm:3200,downshiftRpm:1200,shiftDuration:.45};
    const presets={'small-auto':defaults,'small-manual':{...defaults,preset:'small-manual',transmission:'manual',maxAcceleration:2.4,gearRatios:[3.6,2.1,1.36,1,.82],upshiftRpm:3000,downshiftRpm:1100,shiftDuration:.65},electric:{...defaults,preset:'electric',transmission:'singleSpeed',powertrain:'electric',gearRatios:[1]},'manual-truck':{...defaults,preset:'manual-truck',transmission:'manual',powertrain:'diesel',maxAcceleration:.7,gearRatios:[12.2,9.3,7.2,5.4,4.1,3.1,2.3,1.75,1.3,1]}};
    window.__vehicleSettings=defaults;
    const publish=(action,automatic=false)=>setTimeout(()=>window.ctsMap.receiveHistory({succeeded:true,action,automatic,...window.__history}),0);
    window.chrome={webview:{postMessage(raw){
      const message=JSON.parse(raw);window.__messages.push(message);
      if(message.type==='request_map_settings')setTimeout(()=>window.initializeMapSettings({defaultLocation:'beijing',mapStyle:'default',pathStyle:'theme-neon-blue'}),0);
      if(message.type==='vehicle_settings'){
        if(message.action==='save')window.__vehicleSettings=message.options;
        setTimeout(()=>window.ctsMap.receiveVehicleSettings({succeeded:true,action:message.action,options:window.__vehicleSettings,presets}),0);
      }
      if(message.type!=='map_history')return;
      const history=window.__history;
      if(message.action==='search'){history.searches=history.searches.filter(item=>item.query!==message.query);history.searches.unshift({id:String(Date.now()),query:message.query,createdAtUtc:new Date().toISOString()})}
      if(message.action==='record'){
        // Mock persistence mirrors the route-only identity; cross-coordinate
        // equivalence and migration/backup are covered by the C# store tests.
        const identity=route=>JSON.stringify([route.mode,route.coordinateSystem,route.points.map(p=>[p.lat,p.lng])]);
        const key=identity(message.route),existing=history.routes.find(item=>identity(item.route)===key);
        history.routes=history.routes.filter(item=>identity(item.route)!==key);
        history.routes.unshift({id:existing?.id||String(Date.now()),name:message.automatic&&existing?existing.name:message.name,createdAtUtc:new Date().toISOString(),coordinateSystem:message.route.coordinateSystem,mode:message.route.mode,pointCount:message.route.points.length,speed:message.route.speed,speedDescription:message.route.vehicle?.enabled?`真实驾驶 · 期望 ${message.route.vehicle.averageSpeedKph} / 上限 ${message.route.vehicle.maxSpeedKph} km/h`:`固定 ${message.route.speed} km/h`,route:message.route});
      }
      if(message.action==='rename'){const entry=history.routes.find(item=>item.id===message.id);entry.name=message.name}
      if(message.action==='delete'){const key=message.kind==='search'?'searches':'routes';history[key]=history[key].filter(item=>item.id!==message.id)}
      if(message.action==='clear'){history[message.kind==='search'?'searches':'routes']=[]}
      if(message.action==='load'){const entry=history.routes.find(item=>item.id===message.id);window.loadRouteFile(entry.route)}
      publish(message.action,message.automatic);
    }}};
  });
  try {
    await page.goto(`http://127.0.0.1:${server.address().port}`);
    await page.waitForFunction(()=>window.__testMap&&window.ctsMap);
    const clickMap=async(points)=>page.evaluate(points=>points.forEach(([lat,lng])=>{
      if(window.TMap)window.__testMap.events.click({latLng:new TMap.LatLng(lat,lng)});
      else window.__testMap.fire('click',{latlng:L.latLng(lat,lng)});
    }),points);
    await page.screenshot({path:path.join(output,'planner-desktop.png')});
    await page.locator('#modeSingle').click();
    await clickMap([[30.2,120.1],[30.3,120.2]]);
    assert.equal(await page.locator('#singleSection').isVisible(),true);
    assert.equal(await page.locator('#startButton').isVisible(),false);
    assert.equal(await page.evaluate(()=>window.__testMarkers?window.__testMarkers.geometries.filter(x=>x.id==='selected').length:document.querySelectorAll('.leaflet-marker-icon').length),1);
    await page.locator('#saveRouteButton').click();
    let route=await page.evaluate(()=>window.__messages.filter(x=>x.type==='save_map_route').at(-1).route);
    assert.equal(route.mode,'single');assert.equal(route.points.length,1);assert.equal(route.points[0].lat,30.3);
    // A drag gesture must not turn into a selection on mouse-up.
    const before=await page.evaluate(()=>window.__messages.filter(x=>x.type==='apply').length);
    await page.mouse.move(1000,400);await page.mouse.down();await page.mouse.move(1050,450,{steps:5});await page.mouse.up();
    await clickMap([[10,100]]);
    assert.equal(await page.evaluate(()=>window.__messages.filter(x=>x.type==='apply').length),before);
    assert.equal(await page.evaluate(()=>{const event=new Event('dragstart',{bubbles:true,cancelable:true});document.querySelector('#map').dispatchEvent(event);return event.defaultPrevented}),true);
    // Chinese composition suppresses requests; stale responses cannot replace the current query.
    await page.locator('#searchInput').fill('杭州');await page.waitForTimeout(500);
    const first=await page.evaluate(()=>window.__messages.filter(x=>x.type.endsWith('_suggest')).at(-1));assert.equal(first.query,'杭州');
    await page.locator('#searchInput').fill('上海');await page.waitForTimeout(500);
    await page.evaluate(first=>window.ctsMap.receiveSuggestions({requestId:first.requestId,query:first.query,succeeded:true,results:[{title:'过期地点'}]}),first);
    assert.equal(await page.locator('#searchSuggestions').innerText().then(text=>text.includes('过期地点')),false);
    await page.evaluate(()=>{const request=window.__messages.filter(x=>x.type.endsWith('_suggest')).at(-1);window.ctsMap.receiveSuggestions({requestId:request.requestId,query:request.query,succeeded:true,results:[{id:'poi',title:'上海站',city:'上海市',address:'静安区',latitude:31.25,longitude:121.45}]})});
    assert.equal(await page.locator('#searchInput').evaluate(element=>getComputedStyle(element).outlineStyle),'none');
    await page.locator('#searchInput').press('ArrowDown');await page.locator('#searchInput').press('Enter');
    assert.equal(await page.locator('#singlePointName').innerText(),'上海站');
    await page.locator('#searchHistoryButton').click();await page.waitForTimeout(50);
    assert.match(await page.locator('#historyList').innerText(),/上海站/);
    await page.locator('[data-history-delete]').click();await page.waitForTimeout(50);
    assert.match(await page.locator('#historyList').innerText(),/暂无搜索记录/);await page.locator('#doneHistory').click();
    // Route recording, rename, restore original WGS-84 values and speed, delete confirmation.
    await page.locator('#modeDraw').click();
    await page.waitForTimeout(320);
    await clickMap([[30.2,120.1],[30.3,120.2]]);
    await page.selectOption('#coordinateSystem','wgs84');await page.locator('#convertCoordinate').click();
    await page.locator('#speedInput').fill('88');
    await page.locator('#recordRouteButton').click();await page.locator('#recordName').fill('回归测试轨迹');await page.locator('#saveRecord').click();await page.waitForTimeout(50);
    assert.equal(await page.locator('#recordOverlay').getAttribute('class').then(x=>x.includes('open')),false);
    await page.locator('#routeHistoryButton').click();
    await page.locator('[data-history-rename]').click();await page.locator('#recordName').fill('新名称');await page.locator('#saveRecord').click();await page.waitForTimeout(50);
    assert.match(await page.locator('#historyList').innerText(),/新名称/);
    await page.locator('#doneHistory').click();await page.locator('#clearButton').click();
    await page.selectOption('#coordinateSystem','gcj02');await page.locator('#directCoordinate').click();
    await page.locator('#routeHistoryButton').click();await page.locator('[data-history-use]').click();
    assert.equal(await page.locator('#coordinateSystem').inputValue(),'wgs84');assert.equal(await page.locator('#speedInput').inputValue(),'88');
    await page.locator('#startButton').click();await page.waitForTimeout(50);
    assert.equal(await page.locator('#modeSingle').isDisabled(),true);
    assert.equal(await page.evaluate(()=>window.__history.routes.length),1);
    assert.equal(await page.evaluate(()=>window.__history.routes[0].name),'新名称');
    assert.equal(await page.locator('#recordOverlay').getAttribute('class').then(x=>x.includes('open')),false);
    await page.locator('#stopButton').click();
    await page.locator('#routeHistoryButton').click();await page.locator('#clearHistory').click();await page.locator('#cancelConfirm').click();
    assert.equal(await page.evaluate(()=>window.__history.routes.length),1);
    await page.locator('#clearHistory').click();await page.locator('#acceptConfirm').click();await page.waitForTimeout(50);assert.equal(await page.evaluate(()=>window.__history.routes.length),0);
    await page.locator('#doneHistory').click();
    // Collapse transition retains SVG, focus state and all controls on reopen.
    await page.locator('#collapseButton').click();await page.waitForTimeout(350);
    assert.equal(await page.locator('#collapseButton').getAttribute('aria-expanded'),'false');
    assert.equal(await page.locator('#collapseButton svg').count(),1);
    await page.locator('#collapseButton').click();await page.waitForTimeout(350);
    await page.locator('#modeNavigation').click();await clickMap([[39.9,116.4],[39.91,116.41]]);
    await page.evaluate(()=>{
      const request=window.__messages.filter(x=>x.type==='tencent_route'||x.type==='old_route').at(-1);
      window.ctsMap.receiveRoutes({requestId:request.requestId,succeeded:true,routes:[{distance:11900,durationMinutes:20,trafficLightCount:14,tags:['大众常走','NARROW','LEAST_LIGHT','红绿灯少','UNKNOWN_NEW_ENUM'],points:[{latitude:39.9,longitude:116.4},{latitude:39.91,longitude:116.41}]}]});
    });
    const routeTags=await page.locator('#routeAlternatives').innerText();
    assert.match(routeTags,/途经窄路/);assert.match(routeTags,/红绿灯少/);assert.doesNotMatch(routeTags,/NARROW|LEAST_LIGHT|UNKNOWN_NEW_ENUM/);
    assert.equal(routeTags.split('红绿灯少').length-1,1);
    // Realistic driving profile, input validation, telemetry, and manual controls.
    await page.locator('#vehicleSettingsButton').click();
    await page.selectOption('#vehiclePreset','small-manual');
    await page.locator('#vehicleRandomEvents').check();
    await page.locator('#vehicle-maxSpeedKph').fill('70');await page.locator('#vehicle-averageSpeedKph').fill('80');
    await page.locator('#vehicleSave').click();assert.match(await page.locator('#vehicleError').innerText(),/不能高于/);
    await page.locator('#vehicle-averageSpeedKph').fill('45');await page.locator('#vehicleSave').click();await page.waitForTimeout(50);
    assert.equal(await page.locator('#speedInput').isVisible(),false);
    assert.match(await page.locator('#vehicleSpeedSummary').innerText(),/45.*70/);
    await page.locator('#startButton').click();await page.waitForTimeout(50);
    const simulation=await page.evaluate(()=>window.__messages.filter(x=>x.type==='simulate_path').at(-1));
    assert.equal(simulation.vehicle.transmission,'manual');assert.equal(simulation.vehicle.averageSpeedKph,45);assert.equal(simulation.vehicle.randomEventsEnabled,true);
    assert.equal(await page.locator('#vehicleStatusPanel').isVisible(),true);
    await page.evaluate(()=>window.updateVehicleTelemetry({coordinateSystem:'wgs84',telemetry:{speedKph:32.45,targetSpeedKph:40,gear:3,rpm:2200,throttle:.45,brake:0,controlMode:'auto',transmission:'manual',powertrain:'petrol',phase:'accelerating',constraint:'curve',actualAverageSpeedKph:26}}));
    assert.equal(await page.locator('#vehicleActualSpeed').innerText(),'32.5');assert.equal(await page.locator('#vehicleCoordinate').innerText(),'WGS-84');
    await page.evaluate(()=>window.updateVehicleTelemetry({speedKph:0,targetSpeedKph:0,gear:1,rpm:800,controlMode:'auto',transmission:'manual',powertrain:'petrol',phase:'waiting',constraint:'turnRight',drivingEvent:'pedestrianYield',eventStage:'waiting',eventRemainingSeconds:5.2}));
    assert.match(await page.locator('#vehicleEvent').innerText(),/模拟事件.*右转礼让行人/);assert.match(await page.locator('#vehicleEvent').innerText(),/6 秒/);
    assert.equal(await page.locator('#vehiclePhase').innerText(),'停车等待');
    assert.match(await page.locator('#vehicleConstraint').innerText(),/右转入弯/);
    await page.evaluate(()=>window.updateVehicleTelemetry({speedKph:32.45,targetSpeedKph:40,gear:3,rpm:2200,throttle:.45,controlMode:'auto',transmission:'manual',powertrain:'petrol',constraint:'turnExit'}));
    assert.equal(await page.locator('#vehicleEvent').isVisible(),false);assert.match(await page.locator('#vehicleConstraint').innerText(),/通过弯心/);
    await page.locator('#vehicleManual').click();
    assert.equal(await page.locator('#vehicleThrottle').isDisabled(),false);
    await page.selectOption('#vehicleRuntimeGear','2');
    await page.locator('#vehicleThrottle').fill('75');await page.locator('#vehicleBrake').fill('20');
    const control=await page.evaluate(()=>window.__messages.filter(x=>x.type==='vehicle_control').at(-1).controls);
    assert.deepEqual(control,{automatic:false,gear:2,throttle:.75,brake:.2});
    assert.equal(await page.locator('#vehicleManual').evaluate(button=>button.classList.contains('active')),true);
    assert.equal(await page.locator('#vehicleBrake').isDisabled(),false);
    await page.waitForTimeout(300);
    await page.screenshot({path:path.join(output,'vehicle-driving.png')});
    await page.locator('#vehicleAuto').click();assert.equal(await page.locator('#vehicleThrottle').isDisabled(),true);
    await page.locator('#vehicleStop').click();assert.equal(await page.locator('#vehicleStatusPanel').isVisible(),false);
    await page.locator('#routeHistoryButton').click();
    await page.waitForFunction(()=>document.querySelector('#historyList').textContent.includes('期望 45 / 上限 70'));
    await page.locator('#historyOverlay').waitFor({state:'visible'});
    assert.match(await page.locator('#historyList').textContent(),/真实驾驶.*期望 45.*上限 70/);
    await page.locator('[data-history-use]').first().click();
    assert.equal(await page.evaluate(()=>window.ctsVehicleUi.options.transmission),'manual');
    assert.equal(await page.evaluate(()=>window.ctsVehicleUi.options.randomEventsEnabled),true);
    await page.locator('#startButton').click();
    await page.evaluate(()=>window.ctsMap.simulationAccepted({accepted:false,message:'测试拒绝无效路线'}));
    assert.equal(await page.locator('#vehicleStatusPanel').isVisible(),false);assert.equal(await page.locator('#startButton').isVisible(),true);
    await page.waitForTimeout(350); // Capture the settled UI, not its entrance animation.
    for(const [width,height] of [[1280,800],[900,600],[480,720],[360,640]]){
      await page.setViewportSize({width,height});
      const overflow=await page.evaluate(()=>{
        const planner=document.querySelector('#planner').getBoundingClientRect();
        return [...document.querySelectorAll('.planner-header button,.mode-tab')].some(button=>{const box=button.getBoundingClientRect();return box.right>planner.right+1||box.left<planner.left-1});
      });assert.equal(overflow,false,`Controls overflow at ${width}x${height}`);
      await page.screenshot({path:path.join(output,`planner-${width}x${height}.png`)});
      await page.locator('#routeHistoryButton').click();await page.waitForTimeout(300);
      const dialog=await page.locator('#historyOverlay .dialog').boundingBox();assert.ok(dialog.y>=0&&dialog.y+dialog.height<=height+1);
      await page.screenshot({path:path.join(output,`history-${width}x${height}.png`)});await page.locator('#doneHistory').click();await page.waitForTimeout(350);
      await page.locator('#vehicleSettingsButton').click();
      if(!await page.locator('#vehicleAdvanced').evaluate(details=>details.open))await page.locator('#vehicleAdvanced summary').click();
      await page.waitForTimeout(350);
      const vehicleDialog=await page.locator('#vehicleOverlay .dialog').boundingBox();assert.ok(vehicleDialog.y>=0&&vehicleDialog.y+vehicleDialog.height<=height+1);
      assert.ok(await page.locator('#vehicleSave').isVisible());
      await page.screenshot({path:path.join(output,`vehicle-settings-${width}x${height}.png`)});await page.locator('#vehicleCancel').click();await page.waitForTimeout(250);
      await page.locator('#startButton').click();await page.waitForTimeout(300);
      await page.evaluate(()=>window.updateVehicleTelemetry({speedKph:0,targetSpeedKph:0,gear:1,rpm:800,controlMode:'auto',transmission:'manual',powertrain:'petrol',phase:'waiting',constraint:'turnRight',drivingEvent:'redLight',eventStage:'waiting',eventRemainingSeconds:12}));
      assert.equal(await page.locator('#vehicleEvent').isVisible(),true);
      const panel=await page.locator('#vehicleStatusPanel').boundingBox();assert.ok(panel.x>=0&&panel.x+panel.width<=width+1&&panel.y>=0&&panel.y+panel.height<=height+1);
      await page.screenshot({path:path.join(output,`vehicle-panel-${width}x${height}.png`)});await page.locator('#vehicleStop').click();
    }
    assert.deepEqual(errors,[]);
    console.log(`Map UI (${provider}) checks passed: modes, search, history, coordinates, tags, vehicle settings/validation, telemetry, manual gear/throttle/brake, restore, simulation rejection, drag/collapse and 4 viewport sizes.`);
    console.log(`Screenshots: ${output}`);
  } finally {await browser.close();server.close()}
})().catch(error=>{console.error(error);server.close();process.exitCode=1});
