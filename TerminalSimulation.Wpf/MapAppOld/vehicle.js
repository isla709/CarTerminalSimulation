(() => {
  'use strict';
  const $=id=>document.getElementById(id),clone=value=>JSON.parse(JSON.stringify(value));
  const labels={manual:'手动挡',automatic:'普通自动挡',cvt:'CVT 无级变速',dct:'双离合',singleSpeed:'单速减速器',fixed:'固定速度',petrol:'汽油',diesel:'柴油',electric:'纯电',hybrid:'混合动力',none:'—'};
  const presetNames={'small-manual':'小型手动挡汽车','small-auto':'小型普通自动挡汽车',electric:'电动汽车','manual-truck':'大型手动挡卡车 / 洒水车'};
  const trafficDefaults={minimumGapSeconds:30,allowVirtualSignalsOnStraight:true,virtualSignalSpacingMeters:450,events:{}};
  let trafficCatalog=[];
  function normalizeTraffic(value){const result={...clone(trafficDefaults),...clone(value||{})};result.events={...result.events};for(const item of trafficCatalog)result.events[item.id]={...item.defaults,...result.events[item.id]};return result}
  const defaults={enabled:false,randomEventsEnabled:false,preset:'small-auto',maxSpeedKph:100,averageSpeedKph:50,drivingStyle:'normal',maxAcceleration:2.6,brakeDeceleration:6,transmission:'automatic',powertrain:'petrol',gearRatios:[3.4,2.1,1.4,1,.8,.64],finalDrive:4.1,wheelRadius:.3,idleRpm:800,redlineRpm:6500,upshiftRpm:3200,downshiftRpm:1200,shiftDuration:.45};
  let options=clone(defaults),presets={},running=false,coordinate='gcj02',lastTelemetry=null,dirty=false,opener=null,post=()=>{},notify=()=>{},stop=()=>{};
  let controls={automatic:true,gear:1,throttle:0,brake:0};
  const numberFields={maxSpeedKph:['最大速度（km/h）',5,300,1],averageSpeedKph:['期望平均速度（km/h）',1,300,1],maxAcceleration:['最大加速度（m/s²）',.1,12,.1],brakeDeceleration:['最大刹车减速度（m/s²）',.5,12,.1],finalDrive:['主减速比',1,15,.1],wheelRadius:['车轮半径（m）',.2,1,.01],idleRpm:['怠速转速（rpm）',400,2000,50],redlineRpm:['红线转速（rpm）',1500,16000,100],upshiftRpm:['自动升挡转速（rpm）',600,16000,100],downshiftRpm:['自动降挡转速（rpm）',400,15000,50],shiftDuration:['换挡动力中断时间（s）',.05,3,.05]};
  const field=key=>{const [name,min,max,step]=numberFields[key];return `<label>${name}<input id="vehicle-${key}" type="number" min="${min}" max="${max}" step="${step}" required></label>`};
  document.body.insertAdjacentHTML('beforeend',`
    <div id="vehicleOverlay" class="overlay vehicle-overlay" role="dialog" aria-modal="true" aria-labelledby="vehicleTitle">
      <div class="dialog vehicle-dialog glass"><header><div><strong id="vehicleTitle">真实驾驶模拟</strong><span>车辆能力、驾驶行为与变速箱设置</span></div><button id="vehicleClose" class="icon-button" aria-label="关闭车辆设置"><svg class="icon"><use href="#icon-close"/></svg></button></header>
      <form id="vehicleForm" class="vehicle-form"><label class="vehicle-enable"><input type="checkbox" id="vehicleEnabled"><span><strong>启用真实驾驶速度</strong><small>关闭时使用原有的固定速度模拟</small></span></label>
        <label class="vehicle-preset">车辆预设<select id="vehiclePreset">${Object.entries(presetNames).map(([id,name])=>`<option value="${id}">${name}</option>`).join('')}<option value="custom">自定义车辆</option></select></label>
        <div class="vehicle-fields">${field('maxSpeedKph')}${field('averageSpeedKph')}<label>驾驶风格<select id="vehicle-drivingStyle"><option value="calm">温和 / 平顺</option><option value="normal">普通驾驶</option><option value="sport">积极 / 运动</option></select></label>${field('maxAcceleration')}<label>变速箱类型<select id="vehicle-transmission"><option value="manual">手动挡</option><option value="automatic">普通自动挡</option><option value="cvt">CVT 无级变速</option><option value="dct">双离合</option><option value="singleSpeed">单速减速器</option></select></label><label>动力类型<select id="vehicle-powertrain"><option value="petrol">汽油</option><option value="diesel">柴油</option><option value="electric">纯电</option><option value="hybrid">混合动力</option></select></label>${field('brakeDeceleration')}</div>
        <details id="vehicleAdvanced"><summary>换挡与传动参数</summary><label class="vehicle-ratios">各挡位齿比（从 1 挡起，用逗号分隔）<input id="vehicleGearRatios" type="text" maxlength="150" required></label><div class="vehicle-fields">${['upshiftRpm','downshiftRpm','shiftDuration','finalDrive','wheelRadius','idleRpm','redlineRpm'].map(field).join('')}</div></details>
        <section class="traffic-settings"><label class="vehicle-enable vehicle-events-setting"><input type="checkbox" id="vehicleRandomEvents"><span><strong>启用随机交通情景</strong><small>分阶段模拟路口通行、车流变化和道路干扰；仅自动驾驶操作时生效</small></span></label>
          <div id="trafficSettingsBody"><div class="traffic-presets" aria-label="交通情景预设"><span>快速配置</span><button type="button" data-traffic-preset="light">畅通道路</button><button type="button" data-traffic-preset="daily">日常通勤</button><button type="button" data-traffic-preset="rush">高峰拥堵</button></div>
          <div class="vehicle-fields traffic-global"><label>事件结束后的最小间隔（秒）<input id="trafficMinimumGap" type="number" min="5" max="600" step="1" required></label><label>直路虚拟信号路口最小间距（米）<input id="trafficSignalSpacing" type="number" min="100" max="5000" step="10" required></label></div>
          <label class="traffic-switch"><input id="trafficVirtualSignals" type="checkbox">允许在直路布置虚拟信号路口</label>
          <p class="traffic-help">关闭直路路口后，红灯情景只在轨迹中可识别的较大转向前安排。下方“机会间隔”是满足条件的行驶时间内，平均多久抽签一次；“概率”是每次抽签命中的几率，不是每秒概率。冷却、路况条件和其他情景会降低实际频次，0% 等同关闭。</p>
          <div class="traffic-bulk"><strong>交通情景</strong><button type="button" id="trafficEnableAll">全部启用</button><button type="button" id="trafficDisableAll">全部关闭</button></div><div id="trafficEventList"></div>
          <p class="traffic-help">停车类时间表示每轮停稳后的等待时长；缓行类表示每段受限时长。多轮情景还包含队列前移、起步反应与恢复，不是整个事件的总时长。设置随路线保存，下一次开始模拟时生效。</p></div></section>
        <p class="vehicle-note">平均速度是期望巡航水平。弯道、起步、换挡和终点减速会影响全程均速。预设非实测车型参数；随机事件是虚拟交通场景，不表示地图中真实的行人、信号灯或路况。</p><div id="vehicleError" class="vehicle-error" role="alert"></div>
      </form><footer><span></span><button id="vehicleCancel" class="secondary">取消</button><button id="vehicleSave" class="primary">保存并应用</button></footer></div>
    </div>
    <section id="vehicleStatusPanel" class="vehicle-status glass hidden" aria-label="轨迹模拟驾驶状态">
      <header><div><strong>驾驶状态</strong><small id="vehicleCoordinate">GCJ-02</small></div><span id="vehicleOperationBadge">自动</span><button id="vehiclePanelToggle" class="icon-button" aria-label="展开或收起驾驶状态" aria-expanded="true"><svg class="icon"><use href="#icon-chevron"/></svg></button></header>
      <div class="vehicle-dashboard"><div><b id="vehicleActualSpeed">0.0</b><span>km/h</span></div><span id="vehiclePhase">准备起步</span><button id="vehicleStop" class="quiet-button destructive">停止模拟</button></div>
      <div id="vehicleEvent" class="vehicle-event hidden" role="status"></div>
      <div id="vehiclePanelDetails" class="vehicle-panel-details"><div class="vehicle-metrics"><span>期望 / 上限<b id="vehicleTarget">—</b></span><span>挡位 / 转速<b id="vehicleGearRpm">—</b></span><span>全程平均<b id="vehicleAverage">—</b></span><span>动力 / 变速箱<b id="vehiclePowertrain">—</b></span></div>
        <div class="vehicle-pedals"><label>油门 <b id="vehicleThrottleText">0%</b><meter id="vehicleThrottleMeter" min="0" max="100" value="0"></meter></label><label>刹车 <b id="vehicleBrakeText">0%</b><meter id="vehicleBrakeMeter" min="0" max="100" value="0"></meter></label></div>
        <div id="vehicleManualArea"><div class="vehicle-operation"><button id="vehicleAuto" class="active">自动操作</button><button id="vehicleManual">手动操作</button></div><div id="vehicleManualInputs" class="vehicle-manual-inputs"><label>挡位<select id="vehicleRuntimeGear"></select></label><div class="vehicle-gear-buttons"><button id="vehicleGearDown" class="secondary">− 挡</button><button id="vehicleGearUp" class="secondary">＋ 挡</button></div><label class="pedal-slider">油门开度<input id="vehicleThrottle" aria-label="油门开度" type="range" min="0" max="100" value="0"></label><label class="pedal-slider">刹车力度<input id="vehicleBrake" aria-label="刹车力度" type="range" min="0" max="100" value="0"></label></div></div>
        <div id="vehicleConstraint" class="vehicle-constraint">驾驶模型已就绪</div>
      </div>
    </section>`);

  function updateSummary(){
    $('speedInput').classList.toggle('hidden',options.enabled);$('speedInput').disabled=options.enabled;
    $('speedInput').parentElement.querySelector('em').classList.toggle('hidden',options.enabled);
    $('vehicleSpeedSummary').classList.toggle('hidden',!options.enabled);
    $('vehicleSpeedSummary').textContent=`均 ${options.averageSpeedKph} · 上限 ${options.maxSpeedKph}`;
    $('vehicleSpeedLabel').textContent=options.enabled?'真实驾驶速度':'模拟速度';
    const gearOptions=['<option value="0">N · 空挡</option>',...options.gearRatios.map((_,index)=>`<option value="${index+1}">${index+1} 挡</option>`)];
    $('vehicleRuntimeGear').innerHTML=gearOptions.join('');
  }
  function fillForm(value){
    $('vehicleEnabled').checked=!!value.enabled;$('vehiclePreset').value=presetNames[value.preset]?value.preset:'custom';
    $('vehicleRandomEvents').checked=!!value.randomEventsEnabled;
    $('vehicleRandomEvents').disabled=!value.enabled;
    Object.keys(numberFields).forEach(key=>$(`vehicle-${key}`).value=value[key]);
    ['drivingStyle','transmission','powertrain'].forEach(key=>$(`vehicle-${key}`).value=value[key]);
    $('vehicleGearRatios').value=value.gearRatios.join(', ');$('vehicleError').textContent='';
    fillTraffic(value.trafficEvents);
  }
  function fillTraffic(value){
    const settings=normalizeTraffic(value);
    $('trafficMinimumGap').value=settings.minimumGapSeconds;$('trafficSignalSpacing').value=settings.virtualSignalSpacingMeters;$('trafficVirtualSignals').checked=settings.allowVirtualSignalsOnStraight;
    $('trafficEventList').replaceChildren();
    for(const group of [...new Set(trafficCatalog.map(item=>item.group))]){
      const section=document.createElement('details');section.className='traffic-group';section.open=group===trafficCatalog[0]?.group;
      const summary=document.createElement('summary');summary.textContent=group;section.append(summary);
      for(const item of trafficCatalog.filter(item=>item.group===group)){
        const rule=settings.events[item.id],card=document.createElement('article');card.className='traffic-event-card';
        // Names/descriptions are local application catalog data, never map/POI responses.
        card.innerHTML=`<label class="traffic-event-title"><input id="traffic-${item.id}-enabled" type="checkbox"><strong></strong></label><p></p><div class="traffic-rule-fields"></div>`;
        card.querySelector('strong').textContent=item.name;card.querySelector('p').textContent=item.description;
        card.querySelector('input').checked=rule.enabled;
        const fields=[['probabilityPercent','每次触发概率（%）',0,100,1],['intervalSeconds','平均机会间隔（秒）',5,3600,1],['minDurationSeconds','最短阶段时间（秒）',1,180,1],['maxDurationSeconds','最长阶段时间（秒）',1,180,1]];
        if(item.hasCycles)fields.push(['minCycles','最少轮数',1,6,1],['maxCycles','最多轮数',1,6,1]);
        if(item.hasSlowSpeed)fields.push(['slowSpeedKph','缓行速度（km/h）',2,60,1]);
        for(const [key,label,min,max,step] of fields){const row=document.createElement('label');row.textContent=label;const input=document.createElement('input');Object.assign(input,{id:`traffic-${item.id}-${key}`,type:'number',min,max,step,value:rule[key],required:true});row.append(input);card.querySelector('.traffic-rule-fields').append(row)}
        section.append(card);
      }
      $('trafficEventList').append(section);
    }
    updateTrafficAvailability();
  }
  function collectTraffic(){
    const settings=normalizeTraffic(options.trafficEvents);
    settings.minimumGapSeconds=Number($('trafficMinimumGap').value);settings.virtualSignalSpacingMeters=Number($('trafficSignalSpacing').value);settings.allowVirtualSignalsOnStraight=$('trafficVirtualSignals').checked;
    for(const item of trafficCatalog){const rule=settings.events[item.id];rule.enabled=$(`traffic-${item.id}-enabled`).checked;
      for(const key of ['probabilityPercent','intervalSeconds','minDurationSeconds','maxDurationSeconds',...(item.hasCycles?['minCycles','maxCycles']:[]),...(item.hasSlowSpeed?['slowSpeedKph']:[])])rule[key]=Number($(`traffic-${item.id}-${key}`).value);
      if(rule.maxDurationSeconds<rule.minDurationSeconds)throw new Error(`${item.name}：最长阶段时间不能小于最短阶段时间`);
      if(rule.maxCycles<rule.minCycles)throw new Error(`${item.name}：最多轮数不能小于最少轮数`);
    }
    return settings;
  }
  function updateTrafficAvailability(){
    const enabled=$('vehicleEnabled').checked;$('vehicleRandomEvents').disabled=!enabled;
    $('trafficSettingsBody').classList.toggle('inactive',!enabled||!$('vehicleRandomEvents').checked);
    // Keep controls editable while the master switch is off, so scenarios can be prepared in advance.
    $('trafficSignalSpacing').disabled=!$('trafficVirtualSignals').checked;
  }
  function applyTrafficPreset(name){
    const settings=normalizeTraffic();settings.minimumGapSeconds=name==='rush'?20:name==='light'?60:30;
    for(const item of trafficCatalog){const rule=settings.events[item.id];
      if(name==='light'){rule.intervalSeconds=Math.min(3600,rule.intervalSeconds*2);rule.probabilityPercent=Math.round(rule.probabilityPercent*.5);rule.enabled=!['signalQueue','stopGo','phantomJam','railCrossing'].includes(item.id)}
      if(name==='rush'){rule.intervalSeconds=Math.max(30,Math.round(rule.intervalSeconds*.65));if(['signalQueue','stopGo','phantomJam','slowLeader','mergeAlternating'].includes(item.id))rule.probabilityPercent=85;}
    }
    $('vehicleRandomEvents').checked=true;fillTraffic(settings);
  }
  function open(){if(running)return;opener=document.activeElement;fillForm(options);$('vehicleOverlay').classList.add('open');$('vehicleEnabled').focus()}
  function close(){$('vehicleOverlay').classList.remove('open');opener?.focus()}
  function collect(){
    const value=clone(options);value.enabled=$('vehicleEnabled').checked;value.preset=$('vehiclePreset').value;
    value.randomEventsEnabled=$('vehicleRandomEvents').checked;
    value.trafficEvents=collectTraffic();
    Object.keys(numberFields).forEach(key=>value[key]=Number($(`vehicle-${key}`).value));
    ['drivingStyle','transmission','powertrain'].forEach(key=>value[key]=$(`vehicle-${key}`).value);
    value.gearRatios=$('vehicleGearRatios').value.split(/[,，\s]+/).filter(Boolean).map(Number);
    if(value.transmission==='cvt'||value.transmission==='singleSpeed')value.gearRatios=[1];
    if(value.averageSpeedKph>value.maxSpeedKph)throw new Error('平均速度不能高于最大速度');
    if(!value.gearRatios.length||value.gearRatios.length>12||value.gearRatios.some((ratio,index)=>!Number.isFinite(ratio)||ratio<.1||ratio>20||(index>0&&ratio>=value.gearRatios[index-1])))throw new Error('请填写 1–12 个逐挡递减的有效齿比');
    if(value.downshiftRpm<value.idleRpm||value.upshiftRpm<value.downshiftRpm+200||value.upshiftRpm>value.redlineRpm)throw new Error('换挡转速需满足：怠速 ≤ 降挡 < 升挡 ≤ 红线，升降挡间隔至少 200 rpm');
    return value;
  }
  function sendControls(){if(running&&options.enabled&&options.transmission==='manual')post({type:'vehicle_control',controls:clone(controls)})}
  function switchOperation(automatic){
    controls={automatic,gear:lastTelemetry?.gear||1,throttle:lastTelemetry?.throttle||0,brake:0};
    $('vehicleThrottle').value=Math.round(controls.throttle*100);$('vehicleBrake').value=0;
    updateOperation();sendControls();
  }
  function updateOperation(){
    const manual=options.enabled&&options.transmission==='manual';$('vehicleManualArea').classList.toggle('hidden',!manual);
    $('vehicleAuto').classList.toggle('active',controls.automatic);$('vehicleManual').classList.toggle('active',!controls.automatic);
    $('vehicleManualInputs').classList.toggle('inactive',controls.automatic);
    ['vehicleRuntimeGear','vehicleThrottle','vehicleBrake','vehicleGearDown','vehicleGearUp'].forEach(id=>$(id).disabled=controls.automatic||!running||!manual);
    $('vehicleOperationBadge').textContent=manual&&!controls.automatic?'手动':'自动';
  }
  function setOptions(value){options={...clone(defaults),...clone(value)};options.trafficEvents=normalizeTraffic(options.trafficEvents);dirty=true;updateSummary();updateOperation()}
  function receiveSettings(payload){
    $('vehicleSave').disabled=false;
    if(!payload.succeeded){$('vehicleError').textContent=payload.message||'设置未保存';notify(payload.message||'无法读取车辆设置',true);return}
    presets=payload.presets||presets;
    trafficCatalog=payload.trafficEventCatalog||trafficCatalog;
    if(payload.action==='get'&&dirty)return;
    setOptions(payload.options||defaults);
    if(payload.action==='save'){close();notify(options.enabled?'真实驾驶设置已保存':'已恢复固定速度模式')}
  }
  function start(system){
    running=true;coordinate=system;controls={automatic:true,gear:1,throttle:0,brake:0};lastTelemetry=null;
    $('vehicleStatusPanel').classList.remove('hidden');$('vehicleSettingsButton').disabled=true;$('vehicleSpeedSummary').disabled=true;
    $('vehicleStatusPanel').classList.toggle('compact',innerWidth<=700);$('vehiclePanelToggle').setAttribute('aria-expanded',String(innerWidth>700));
    document.body.classList.add('vehicle-running');updateOperation();render({speedKph:options.enabled?0:Number($('speedInput').value)||60,targetSpeedKph:options.enabled?options.averageSpeedKph:Number($('speedInput').value)||60,gear:1,rpm:options.idleRpm,transmission:options.enabled?options.transmission:'fixed',powertrain:options.enabled?options.powertrain:'none',phase:'stopped',constraint:'cruise',throttle:0,brake:0});
  }
  function finish(){running=false;$('vehicleStatusPanel').classList.add('hidden');document.body.classList.remove('vehicle-running');$('vehicleSettingsButton').disabled=false;$('vehicleSpeedSummary').disabled=false;updateOperation()}
  function render(payload){
    if(!running)return;
    const value=payload.telemetry||payload;lastTelemetry=value;if(payload.coordinateSystem)coordinate=payload.coordinateSystem;
    $('vehicleCoordinate').textContent=coordinate==='wgs84'?'WGS-84':coordinate==='bd09'?'BD-09':'GCJ-02';
    $('vehicleActualSpeed').textContent=Math.max(0,Number(value.speedKph)||0).toFixed(1);
    $('vehicleTarget').textContent=options.enabled?`${Number(value.targetSpeedKph||0).toFixed(0)} / ${options.maxSpeedKph} km/h`:`${Number(value.targetSpeedKph||0).toFixed(0)} km/h`;
    const gearLabel=value.gear===0?'N':value.transmission==='cvt'?'D · 无级':value.transmission==='singleSpeed'?'单速':value.gear+' 挡';
    $('vehicleGearRpm').textContent=value.transmission==='fixed'?'—':`${gearLabel} · ${Math.round(value.rpm||0)} rpm`;
    $('vehicleAverage').textContent=value.transmission==='fixed'?'—':Number(value.actualAverageSpeedKph||0).toFixed(1)+' km/h';
    $('vehiclePowertrain').textContent=`${labels[value.powertrain]||'—'} · ${labels[value.transmission]||'—'}`;
    const phase={stopped:'准备起步',waiting:'停车等待',accelerating:'加速行驶',cruising:'稳定巡航',coasting:'滑行减速',braking:'刹车减速',shifting:'换挡中',neutral:'空挡滑行',finished:'已到达终点'};
    $('vehiclePhase').textContent=phase[value.phase]||'模拟中';
    for(const pedal of ['Throttle','Brake']){const percent=Math.round(Math.max(0,Math.min(1,Number(value[pedal.toLowerCase()])||0))*100);$(`vehicle${pedal}Text`).textContent=percent+'%';$(`vehicle${pedal}Meter`).value=percent}
    $('vehicleRuntimeGear').value=String(value.gear||0);
    $('vehicleConstraint').textContent=({curve:'弯道限速 / 提前减速',turnLeft:'左转入弯 · 提前减速',turnRight:'右转入弯 · 提前减速',turnExit:'通过弯心 · 平顺加速出弯',finish:'接近终点，平顺停车',cruise:'按驾驶风格调节巡航',rpmLimit:'转速达到红线，限制动力',shiftProtected:'降挡保护：预计转速超过红线',fixed:'固定速度行驶'})[value.constraint]||'模拟中';
    const eventName=trafficCatalog.find(item=>item.id===value.drivingEvent)?.name;
    $('vehicleEvent').classList.toggle('hidden',!eventName);
    const stages={approaching:'提前减速 · 接近等待点',braking:'制动至停稳',red:'红灯等待',redQueue:'红灯排队等待',startDelay:'前车起步 · 反应等待',queueMove:'跟随队列前移',queueStop:'拥堵停车',mergeWait:'交替合流让行',releasing:'队列放行 · 平顺起步',compression:'车流压缩 · 减速波',waveRelease:'拥堵波缓解 · 短暂恢复',following:'随前车缓行',gapOpening:'收油减速 · 拉开车距',recovering:'车距恢复 · 平顺加速',oncomingWait:'等待对向车流间隙',pedestrianWait:'等待行人通过',obstructionWait:'等待前方占道解除',workzoneWait:'施工入口等待放行',emergencyWait:'停车避让应急车辆',barrierWait:'等待道口栏杆开启',narrowPass:'低速通过施工窄道',cautiousPass:'观察后缓慢通过',bumpApproach:'接近减速带 / 破损路面',bumpPass:'低速通过颠簸路段'};
    const cycle=value.eventTotalCycles>1?` · 第 ${value.eventCycle}/${value.eventTotalCycles} 轮`:'';
    const time=value.eventRemainingSeconds>0?` · ${['approaching','braking'].includes(value.eventStage)?'停稳后等待 ':''}${Math.ceil(value.eventRemainingSeconds)} 秒`:'';
    $('vehicleEvent').textContent=eventName?`模拟情景 · ${eventName}${cycle}\n${stages[value.eventStage]||'处理中'}${time}`:'';
    if(value.controlMode)controls.automatic=value.controlMode!=='manual';
    if(controls.automatic||value.constraint==='shiftProtected')controls.gear=value.gear||0;
    updateOperation();
  }
  $('vehiclePreset').onchange=()=>{const preset=presets[$('vehiclePreset').value];if(preset){const traffic=collectTraffic();fillForm({...clone(preset),enabled:true,randomEventsEnabled:$('vehicleRandomEvents').checked,trafficEvents:traffic})}};
  $('vehicleEnabled').onchange=$('vehicleRandomEvents').onchange=$('trafficVirtualSignals').onchange=updateTrafficAvailability;
  document.querySelectorAll('[data-traffic-preset]').forEach(button=>button.onclick=()=>applyTrafficPreset(button.dataset.trafficPreset));
  for(const [id,enabled] of [['trafficEnableAll',true],['trafficDisableAll',false]])$(id).onclick=()=>{for(const item of trafficCatalog)$(`traffic-${item.id}-enabled`).checked=enabled};
  $('vehicleSettingsButton').onclick=$('vehicleSpeedSummary').onclick=open;
  $('vehicleClose').onclick=$('vehicleCancel').onclick=close;
  $('vehicleSave').onclick=()=>{if(!$('vehicleForm').reportValidity())return;try{const value=collect();$('vehicleSave').disabled=true;post({type:'vehicle_settings',action:'save',options:value})}catch(error){$('vehicleError').textContent=error.message}};
  $('vehicleForm').onsubmit=event=>{event.preventDefault();$('vehicleSave').click()};
  $('vehicleOverlay').onclick=event=>{if(event.target===$('vehicleOverlay'))close()};
  $('vehiclePanelToggle').onclick=()=>{const compact=$('vehicleStatusPanel').classList.toggle('compact');$('vehiclePanelToggle').setAttribute('aria-expanded',String(!compact))};
  $('vehicleStop').onclick=()=>stop();$('vehicleAuto').onclick=()=>switchOperation(true);$('vehicleManual').onclick=()=>switchOperation(false);
  $('vehicleRuntimeGear').onchange=()=>{controls.gear=Number($('vehicleRuntimeGear').value);sendControls()};
  const shift=amount=>{controls.gear=Math.max(0,Math.min(options.gearRatios.length,controls.gear+amount));$('vehicleRuntimeGear').value=controls.gear;sendControls()};
  $('vehicleGearDown').onclick=()=>shift(-1);$('vehicleGearUp').onclick=()=>shift(1);
  ['Throttle','Brake'].forEach(pedal=>$(`vehicle${pedal}`).oninput=()=>{controls[pedal.toLowerCase()]=Number($(`vehicle${pedal}`).value)/100;sendControls()});
  window.ctsVehicleUi={init(send,toast,onStop){post=send;notify=toast;stop=onStop;post({type:'vehicle_settings',action:'get'})},get options(){return clone(options)},receiveSettings,setOptions,start,finish,render,closeSettings:close};
  window.updateVehicleTelemetry=render;updateSummary();updateOperation();
})();
