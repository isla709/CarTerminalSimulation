
import common from '@ohos.app.ability.common';
import { FlutterPage } from '@ohos/flutter_ohos'
import deviceInfo from '@ohos.deviceInfo';
import { CtsChromeHost, chromeState } from '../platform/CtsChromeHost';

let storage = LocalStorage.getShared()
const EVENT_BACK_PRESS = 'EVENT_BACK_PRESS'

@Entry(storage)
@Component
struct Index {
  private context = getContext(this) as common.UIAbilityContext
  @LocalStorageLink('viewId') viewId: string = "";

  build() {
    Stack({ alignContent: Alignment.TopStart }) {
      FlutterPage({ viewId: this.viewId,
        safeAreaEdges: [SafeAreaEdge.TOP, SafeAreaEdge.BOTTOM], safeAreaTypes: [SafeAreaType.SYSTEM] })
        .width('100%').height('100%')
      if (deviceInfo.sdkApiVersion >= 26) {
        CtsChromeHost({ state: chromeState })
      }
    }.width('100%').height('100%')
      .expandSafeArea([SafeAreaType.SYSTEM], [SafeAreaEdge.TOP, SafeAreaEdge.BOTTOM])
  }

  onBackPress(): boolean {
    this.context.eventHub.emit(EVENT_BACK_PRESS)
    return true
  }
}
