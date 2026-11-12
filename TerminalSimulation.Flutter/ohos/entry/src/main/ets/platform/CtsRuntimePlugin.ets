import { FlutterPlugin, FlutterPluginBinding, MethodCall, MethodCallHandler, MethodChannel, MethodResult } from '@ohos/flutter_ohos';
import common from '@ohos.app.ability.common';
import abilityAccessCtrl from '@ohos.abilityAccessCtrl';
import geoLocationManager from '@ohos.geoLocationManager';
import backgroundTaskManager from '@ohos.resourceschedule.backgroundTaskManager';
import wantAgent from '@ohos.app.ability.wantAgent';
import systemDateTime from '@ohos.systemDateTime';
import bundleManager from '@ohos.bundle.bundleManager';
import { BusinessError } from '@ohos.base';
import deviceInfo from '@ohos.deviceInfo';

export default class CtsRuntimePlugin implements FlutterPlugin, MethodCallHandler {
  private channel: MethodChannel | null = null;
  private connection: boolean = false;
  private positioning: boolean = false;
  private task: boolean = false;
  private busy: boolean = false;
  private disposing: boolean = false;
  private satellites: number = -1;
  private satellitesUsed: number = -1;
  private cancelWatching: boolean = false;
  private context: common.UIAbilityContext;
  constructor(context: common.UIAbilityContext) { this.context = context; }
  getUniqueClassName(): string { return 'CtsRuntimePlugin'; }
  onAttachedToEngine(binding: FlutterPluginBinding): void {
    this.channel = new MethodChannel(binding.getBinaryMessenger(), 'cts/runtime');
    this.channel.setMethodCallHandler(this);
    try { backgroundTaskManager.on('continuousTaskCancel', this.cancelled); this.cancelWatching = true; } catch (_) {}
  }
  onDetachedFromEngine(binding: FlutterPluginBinding): void {
    this.disposing = true;
    if (this.cancelWatching) backgroundTaskManager.off('continuousTaskCancel', this.cancelled);
    this.stopLocation();
    if (this.task) backgroundTaskManager.stopBackgroundRunning(this.context).catch(() => {});
    this.channel?.setMethodCallHandler(null);
    this.channel = null;
  }
  onMethodCall(call: MethodCall, result: MethodResult): void {
    if (call.method !== 'configure') { result.notImplemented(); return; }
    if (this.busy) { result.error('busy', '后台任务处理中', null); return; }
    this.busy = true;
    const connection = call.argument('connection') as boolean;
    const positioning = call.argument('location') as boolean;
    this.configure(connection, positioning).then(() => result.success(null)).catch((error: BusinessError) => {
      result.error('runtime-unavailable', error.message || '后台运行或定位不可用，请检查系统权限', null);
    }).finally(() => { this.busy = false; });
  }
  private async configure(connection: boolean, positioning: boolean): Promise<void> {
    if (this.disposing) throw new Error('后台任务已关闭');
    if (positioning && !this.positioning) {
      const grant = await abilityAccessCtrl.createAtManager().requestPermissionsFromUser(this.context,
        ['ohos.permission.APPROXIMATELY_LOCATION', 'ohos.permission.LOCATION']);
      if (grant.authResults[0] !== 0) throw new Error('请授予位置权限后使用设备定位');
      if (!geoLocationManager.isLocationEnabled()) throw new Error('请先在系统中开启定位');
    }
    if (this.disposing) throw new Error('定位操作已取消');
    const modes: string[] = [];
    if (connection) modes.push('dataTransfer');
    if (positioning) modes.push('location');
    if (modes.length === 0) {
      this.stopLocation();
      this.connection = false;
      this.positioning = false;
      if (this.task) await backgroundTaskManager.stopBackgroundRunning(this.context);
      this.task = false;
      return;
    }
    if (this.task) {
      await backgroundTaskManager.updateBackgroundRunning(this.context, modes);
    } else {
      const info = await bundleManager.getBundleInfoForSelf(bundleManager.BundleFlag.GET_BUNDLE_INFO_DEFAULT);
      const agent = await wantAgent.getWantAgent({
        wants: [{ bundleName: info.name, abilityName: 'EntryAbility' }],
        operationType: wantAgent.OperationType.START_ABILITY,
        requestCode: 10783, wantAgentFlags: [wantAgent.WantAgentFlags.UPDATE_PRESENT_FLAG]
      });
      await backgroundTaskManager.startBackgroundRunning(this.context, modes, agent);
      this.task = true;
    }
    const previousLocation = this.positioning;
    this.connection = connection;
    this.positioning = positioning;
    try {
      if (positioning && !previousLocation) {
        const request: geoLocationManager.LocationRequest = {
          priority: geoLocationManager.LocationRequestPriority.ACCURACY,
          scenario: geoLocationManager.LocationRequestScenario.UNSET,
          timeInterval: 1, distanceInterval: 0, maxAccuracy: 0
        };
        geoLocationManager.on('locationChange', request, this.position);
        try { geoLocationManager.on('locationError', this.locationError); } catch (_) {}
        try { geoLocationManager.on('locationEnabledChange', this.locationEnabled); } catch (_) {}
        try { geoLocationManager.on('satelliteStatusChange', this.satellite); } catch (_) {}
        this.emitStatus('等待系统定位，可使用卫星、网络或室内定位');
      } else if (!positioning && previousLocation) this.stopLocation();
    } catch (error) {
      this.positioning = false;
      this.stopLocation();
      if (!connection && this.task) {
        await backgroundTaskManager.stopBackgroundRunning(this.context);
        this.task = false;
      }
      throw new Error((error as BusinessError).message || '系统定位启动失败');
    }
  }
  private stopLocation(): void {
    try { geoLocationManager.off('locationChange', this.position); } catch (_) {}
    try { geoLocationManager.off('satelliteStatusChange', this.satellite); } catch (_) {}
    try { geoLocationManager.off('locationError', this.locationError); } catch (_) {}
    try { geoLocationManager.off('locationEnabledChange', this.locationEnabled); } catch (_) {}
    this.satellites = -1;
    this.satellitesUsed = -1;
  }
  private position = (fix: geoLocationManager.Location): void => {
    if (!this.positioning || this.disposing) return;
    const event = new Map<string, Object>();
    event.set('type', 'fix');
    event.set('latitude', fix.latitude); event.set('longitude', fix.longitude);
    if (Number.isFinite(fix.altitude) && fix.altitudeAccuracy !== undefined && fix.altitudeAccuracy >= 0) event.set('altitude', fix.altitude);
    if (Number.isFinite(fix.speed) && fix.speed >= 0) event.set('speed', fix.speed);
    if (Number.isFinite(fix.direction) && fix.direction >= 0) event.set('bearing', fix.direction);
    event.set('accuracy', fix.accuracy);
    const timestamp = fix.timeStamp > 1000000000000 ? fix.timeStamp : fix.timeStamp * 1000;
    event.set('time', timestamp);
    event.set('ageMs', fix.timeSinceBoot > 0
      ? (systemDateTime.getUptime(systemDateTime.TimeType.STARTUP, true) - fix.timeSinceBoot) / 1000000
      : Math.max(0, Date.now() - timestamp));
    let source: string = 'unknown';
    switch (fix.sourceType) {
      case geoLocationManager.LocationSourceType.GNSS: source = 'gnss'; break;
      case geoLocationManager.LocationSourceType.RTK: source = 'rtk'; break;
      case geoLocationManager.LocationSourceType.NETWORK: source = 'network'; break;
      case geoLocationManager.LocationSourceType.INDOOR: source = 'indoor'; break;
    }
    event.set('source', source);
    let mock: boolean = false;
    if (deviceInfo.sdkApiVersion >= 26) mock = fix.isFromMock === true;
    event.set('mock', mock);
    this.channel?.invokeMethod('event', event);
  }
  private satellite = (info: geoLocationManager.SatelliteStatusInfo): void => {
    if (!this.positioning || this.disposing) return;
    this.satellites = info.satellitesNumber;
    const flags = info.satelliteAdditionalInfo;
    this.satellitesUsed = flags !== undefined && flags.length === info.satellitesNumber
      ? flags.filter((flag: number) => (flag & geoLocationManager.SatelliteAdditionalInfo.SATELLITES_ADDITIONAL_INFO_USED_IN_FIX) !== 0).length : -1;
    const event = new Map<string, Object>();
    event.set('type', 'satellites'); event.set('visible', this.satellites);
    if (this.satellitesUsed >= 0) event.set('used', this.satellitesUsed);
    this.channel?.invokeMethod('event', event);
  }
  private locationEnabled = (enabled: boolean): void => {
    if (!this.positioning || this.disposing) return;
    this.emitStatus(enabled ? '系统定位已开启，等待新位置' : '系统定位已关闭', !enabled);
  }
  private locationError = (error: geoLocationManager.LocationError): void => {
    if (!this.positioning || this.disposing) return;
    switch (error) {
      case geoLocationManager.LocationError.LOCATING_FAILED_LOCATION_SWITCH_OFF:
        this.emitStatus('系统定位已关闭', true); break;
      case geoLocationManager.LocationError.LOCATING_FAILED_LOCATION_PERMISSION_DENIED:
        this.emitStatus('位置权限不可用，请在系统设置中检查授权', true); break;
      case geoLocationManager.LocationError.LOCATING_FAILED_BACKGROUND_PERMISSION_DENIED:
        this.emitStatus('系统暂停了后台定位，请回到应用检查后台运行权限', true); break;
      case geoLocationManager.LocationError.LOCATING_FAILED_INTERNET_ACCESS_FAILURE:
        this.emitStatus('网络定位服务无法联网，仍在等待其他可用位置'); break;
      default: this.emitStatus('系统暂未获得位置，请检查定位开关、网络或稍后重试');
    }
  }
  private cancelled = (info: backgroundTaskManager.ContinuousTaskCancelInfo): void => {
    if (!this.task || this.busy || this.disposing) return;
    this.task = false;
    this.connection = false; this.positioning = false;
    this.stopLocation();
    const event = new Map<string, Object>();
    event.set('type', 'stop'); event.set('message', '系统已停止后台任务，请回到应用重新启用');
    this.channel?.invokeMethod('event', event);
  }
  private emitStatus(message: string, invalidate: boolean = false): void {
    const event = new Map<string, Object>();
    event.set('type', 'status'); event.set('message', message);
    event.set('invalidate', invalidate);
    this.channel?.invokeMethod('event', event);
  }
}
