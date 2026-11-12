import { FlutterPlugin, FlutterPluginBinding, MethodCall, MethodCallHandler,
  MethodChannel, MethodResult } from '@ohos/flutter_ohos';
import deviceInfo from '@ohos.deviceInfo';
import uiMaterial from '@ohos.arkui.uiMaterial';
import { chromeState } from './CtsChromeHost';

export default class CtsChromePlugin implements FlutterPlugin, MethodCallHandler {
  private channel: MethodChannel | null = null;
  getUniqueClassName(): string { return 'CtsChromePlugin'; }
  onAttachedToEngine(binding: FlutterPluginBinding): void {
    this.channel = new MethodChannel(binding.getBinaryMessenger(), 'cts/chrome');
    this.channel.setMethodCallHandler(this);
    chromeState.onReady = (generation: number) => this.channel?.invokeMethod('chromeReady', generation);
    chromeState.onSelected = (index: number) => this.channel?.invokeMethod('selected', index);
    chromeState.onMounted = () => this.channel?.invokeMethod('chromeHostReady', null);
  }
  onDetachedFromEngine(binding: FlutterPluginBinding): void {
    chromeState.enabled = false;
    chromeState.onReady = () => {};
    chromeState.onSelected = () => {};
    chromeState.onMounted = () => {};
    this.channel?.setMethodCallHandler(null);
    this.channel = null;
  }
  private number(call: MethodCall, key: string, fallback: number = 0): number {
    const value = call.argument(key) as number;
    return Number.isFinite(value) ? value : fallback;
  }
  onMethodCall(call: MethodCall, result: MethodResult): void {
    if (deviceInfo.sdkApiVersion < 26) { result.error('unsupported', 'API 26 required', null); return; }
    if (call.method === 'hide') {
      if (chromeState.owner === this.number(call, 'owner')) chromeState.enabled = false;
      result.success(null);
      return;
    }
    if (call.method !== 'configure') { result.notImplemented(); return; }
    const headerX = this.number(call, 'headerX'), headerY = this.number(call, 'headerY');
    const headerWidth = this.number(call, 'headerWidth'), headerHeight = this.number(call, 'headerHeight');
    const bottomX = this.number(call, 'bottomX'), bottomY = this.number(call, 'bottomY');
    const bottomWidth = this.number(call, 'bottomWidth'), bottomHeight = this.number(call, 'bottomHeight');
    const enabled = call.argument('enabled') === true;
    const bottomVisible = call.argument('bottomVisible') === true;
    if (chromeState.headerX !== headerX ||
        chromeState.headerY !== headerY || chromeState.headerWidth !== headerWidth ||
        chromeState.headerHeight !== headerHeight) chromeState.headerLaidOut = false;
    if (chromeState.bottomX !== bottomX ||
        chromeState.bottomY !== bottomY || chromeState.bottomWidth !== bottomWidth ||
        chromeState.bottomHeight !== bottomHeight) chromeState.bottomLaidOut = false;
    chromeState.headerX = headerX; chromeState.headerY = headerY;
    chromeState.headerWidth = headerWidth; chromeState.headerHeight = headerHeight;
    chromeState.bottomX = bottomX; chromeState.bottomY = bottomY;
    chromeState.bottomWidth = bottomWidth; chromeState.bottomHeight = bottomHeight;
    chromeState.owner = this.number(call, 'owner');
    chromeState.generation = this.number(call, 'generation');
    chromeState.selected = Math.max(0, Math.min(4, this.number(call, 'selected')));
    chromeState.status = call.argument('status') as string ?? '未连接';
    chromeState.connected = call.argument('connected') === true;
    chromeState.compact = call.argument('compact') === true;
    chromeState.dark = call.argument('dark') === true;
    chromeState.materialSupported = uiMaterial.isImmersiveMaterialSupported();
    chromeState.reduceMotion = call.argument('reduceMotion') === true;
    chromeState.iconOnly = call.argument('iconOnly') === true;
    chromeState.textScale = Math.max(1, Math.min(3, this.number(call, 'textScale', 1)));
    chromeState.cornerRadius = this.number(call, 'cornerRadius', 28);
    chromeState.contentInset = this.number(call, 'contentInset', 6);
    chromeState.bottomVisible = bottomVisible;
    chromeState.enabled = enabled;
    const answer = new Map<string, Object>();
    answer.set('ready', chromeState.ready());
    result.success(answer);
  }
}
