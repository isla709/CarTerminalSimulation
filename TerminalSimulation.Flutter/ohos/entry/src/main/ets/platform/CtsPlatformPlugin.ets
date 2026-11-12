import { FlutterPlugin, FlutterPluginBinding, MethodCall, MethodCallHandler, MethodChannel, MethodResult } from '@ohos/flutter_ohos';
import deviceInfo from '@ohos.deviceInfo';
import promptAction from '@ohos.promptAction';
import { BusinessError } from '@ohos.base';
import common from '@ohos.app.ability.common';
import picker from '@ohos.file.picker';
import fs from '@ohos.file.fs';
import uiMaterial from '@ohos.arkui.uiMaterial';

// A compiled platform adapter, unrelated to the excluded dynamic CTS plugin system.
export default class CtsPlatformPlugin implements FlutterPlugin, MethodCallHandler {
  private channel: MethodChannel | null = null;
  private context: common.UIAbilityContext;
  private saving: boolean = false;

  constructor(context: common.UIAbilityContext) { this.context = context; }
  getUniqueClassName(): string { return 'CtsPlatformPlugin'; }
  onAttachedToEngine(binding: FlutterPluginBinding): void {
    this.channel = new MethodChannel(binding.getBinaryMessenger(), 'cts/platform');
    this.channel.setMethodCallHandler(this);
  }
  onDetachedFromEngine(binding: FlutterPluginBinding): void {
    this.channel?.setMethodCallHandler(null);
    this.channel = null;
  }
  onMethodCall(call: MethodCall, result: MethodResult): void {
    if (call.method === 'dataDirectory') {
      result.success(this.context.filesDir);
    } else if (call.method === 'openVideo') {
      if (this.saving) { result.error('busy', '文件选择器正在使用', null); return; }
      this.saving = true;
      this.openVideo().then((path: string | null) => result.success(path))
        .catch(() => result.error('video-open-failed', '无法读取视频，请选择不超过 256 MiB 的 H.264 裸流文件', null))
        .finally(() => { this.saving = false; });
    } else if (call.method === 'openDocument') {
      if (this.saving) { result.error('busy', '文件选择器正在使用', null); return; }
      this.saving = true;
      this.openDocument().then((bytes: Uint8Array | null) => {
        result.success(bytes);
      }).catch((error: BusinessError) => {
        result.error('open-failed', '文件读取失败或路线超过 25 MiB', null);
      }).finally(() => { this.saving = false; });
    } else if (call.method === 'saveDocument') {
      if (this.saving) { result.error('busy', '文件选择器正在使用', null); return; }
      const bytes = call.argument('bytes') as Uint8Array;
      const name = call.argument('name') as string;
      if (!bytes || bytes.byteLength > 25 * 1024 * 1024) {
        result.error('invalid-data', '文件内容无效或超过 25 MiB', null); return;
      }
      this.saving = true;
      this.saveDocument(bytes, name).then((saved: boolean) => {
        result.success(saved);
      }).catch((error: BusinessError) => {
        result.error('save-failed', '文件保存失败', null);
      }).finally(() => { this.saving = false; });
    } else if (call.method === 'materialCapabilities') {
      const capability = new Map<string, Object>();
      capability.set('platform', 'harmonyos');
      capability.set('apiVersion', deviceInfo.sdkApiVersion);
      capability.set('nativeDialogs', deviceInfo.sdkApiVersion >= 26);
      capability.set('nativeChrome', deviceInfo.sdkApiVersion >= 26);
      if (deviceInfo.sdkApiVersion >= 26) {
        capability.set('immersiveMaterialSupported', uiMaterial.isImmersiveMaterialSupported());
        capability.set('materialLevel', uiMaterial.getGlobalMaterialLevel());
      }
      result.success(capability);
    } else if (call.method === 'showMaterialDialog') {
      // The entry module opts in to system materials. No API 26-only calls run on older systems.
      if (deviceInfo.sdkApiVersion < 26) { result.error('unsupported', 'Native material requires API 26', null); return; }
      const title = call.argument('title') as string;
      const message = call.argument('message') as string;
      promptAction.showDialog({
        title: title,
        message: message,
        buttons: [{ text: '取消', color: '#607078' }, { text: '确定', color: '#087F8C' }]
      }).then((answer: promptAction.ShowDialogSuccessResponse) => {
        result.success(answer.index === 1);
      }).catch((error: BusinessError) => {
        result.error('native-dialog', error.message, null);
      });
    } else {
      result.notImplemented();
    }
  }
  private async saveDocument(bytes: Uint8Array, name: string): Promise<boolean> {
    const dialog = new picker.DocumentViewPicker(this.context);
    const paths = await dialog.save({ newFileNames: [name], fileSuffixChoices: ['CTS路线|.ctsroute'] });
    if (paths.length === 0) return false;
    const file = await fs.open(paths[0], fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE | fs.OpenMode.TRUNC);
    try {
      let written = 0;
      while (written < bytes.byteLength) {
        const part = bytes.slice(written).buffer as ArrayBuffer;
        const count = await fs.write(file.fd, part);
        if (count <= 0) throw new Error('文件写入失败');
        written += count;
      }
      return true;
    } finally { await fs.close(file); }
  }
  private async openDocument(): Promise<Uint8Array | null> {
    const dialog = new picker.DocumentViewPicker(this.context);
    const paths = await dialog.select({ maxSelectNumber: 1 });
    if (paths.length === 0) return null;
    const file = await fs.open(paths[0], fs.OpenMode.READ_ONLY);
    try {
      const info = await fs.stat(file.fd);
      if (info.size > 25 * 1024 * 1024) throw new Error('路线超过 25 MiB');
      const chunks: Uint8Array[] = [];
      let total = 0;
      while (true) {
        const buffer = new ArrayBuffer(65536);
        const count = await fs.read(file.fd, buffer);
        if (count === 0) break;
        total += count;
        if (total > 25 * 1024 * 1024) throw new Error('路线超过 25 MiB');
        chunks.push(new Uint8Array(buffer, 0, count));
      }
      const bytes = new Uint8Array(total);
      let offset = 0;
      for (const part of chunks) { bytes.set(part, offset); offset += part.byteLength; }
      return bytes;
    } finally { await fs.close(file); }
  }
  private async openVideo(): Promise<string | null> {
    const paths = await new picker.DocumentViewPicker(this.context).select({
      maxSelectNumber: 1, fileSuffixFilters: ['H.264|.h264,.264,.avc']
    });
    if (paths.length === 0) return null;
    const source = await fs.open(paths[0], fs.OpenMode.READ_ONLY);
    const directory = this.context.filesDir + '/video-sources';
    let destination = '';
    try {
      if ((await fs.stat(source.fd)).size > 256 * 1024 * 1024) throw new Error('视频超过 256 MiB');
      await fs.mkdir(directory, true);
      destination = directory + '/video-' + Date.now() + '.h264';
      await fs.copyFile(source.fd, destination);
      if ((await fs.stat(destination)).size > 256 * 1024 * 1024) throw new Error('视频超过 256 MiB');
      return destination;
    } catch (error) {
      if (destination.length > 0) { try { await fs.unlink(destination); } catch (_) {} }
      throw new Error('视频读取失败');
    } finally { await fs.close(source); }
  }
}
