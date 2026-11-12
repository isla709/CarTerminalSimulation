
import { FlutterAbility, FlutterEngine } from '@ohos/flutter_ohos';
import { GeneratedPluginRegistrant } from '../plugins/GeneratedPluginRegistrant';
import CtsPlatformPlugin from '../platform/CtsPlatformPlugin';
import CtsRuntimePlugin from '../platform/CtsRuntimePlugin';
import CtsChromePlugin from '../platform/CtsChromePlugin';

export default class EntryAbility extends FlutterAbility {
  configureFlutterEngine(flutterEngine: FlutterEngine) {
    super.configureFlutterEngine(flutterEngine)
    GeneratedPluginRegistrant.registerWith(flutterEngine)
    flutterEngine.getPlugins()?.add(new CtsPlatformPlugin(this.context))
    flutterEngine.getPlugins()?.add(new CtsRuntimePlugin(this.context))
    flutterEngine.getPlugins()?.add(new CtsChromePlugin())
  }
}
