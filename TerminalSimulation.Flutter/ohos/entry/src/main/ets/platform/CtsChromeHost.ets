import uiMaterial from '@ohos.arkui.uiMaterial';
import { CommonModifier } from '@ohos.arkui.modifier';

@Observed
export class ChromeState {
  mounted: boolean = false;
  enabled: boolean = false;
  bottomVisible: boolean = false;
  owner: number = 0;
  generation: number = 0;
  selected: number = 0;
  compact: boolean = true;
  dark: boolean = false;
  materialSupported: boolean = true;
  connected: boolean = false;
  status: string = '未连接';
  reduceMotion: boolean = false;
  iconOnly: boolean = false;
  textScale: number = 1;
  cornerRadius: number = 28;
  contentInset: number = 6;
  headerX: number = 0;
  headerY: number = 0;
  headerWidth: number = 0;
  headerHeight: number = 56;
  bottomX: number = 0;
  bottomY: number = 0;
  bottomWidth: number = 0;
  bottomHeight: number = 72;
  headerLaidOut: boolean = false;
  bottomLaidOut: boolean = false;
  onReady: (generation: number) => void = () => {};
  onSelected: (index: number) => void = () => {};
  onMounted: () => void = () => {};
  ready(): boolean {
    return this.mounted && this.enabled && this.headerLaidOut &&
      (!this.bottomVisible || this.bottomLaidOut);
  }
  select(index: number): void {
    if (!this.enabled || !this.bottomVisible || !Number.isInteger(index) ||
        index < 0 || index >= 5 || index === this.selected) return;
    this.selected = index;
    this.onSelected(index);
  }
  markReady(header: boolean, area: Area): void {
    if (Number(area.width) <= 0 || Number(area.height) <= 0) return;
    if (header) this.headerLaidOut = true;
    else this.bottomLaidOut = true;
    if (this.ready()) this.onReady(this.generation);
  }
}
export const chromeState = new ChromeState();

// Native siblings of FlutterPage, not a PlatformView/texture snapshot.
@Component
export struct CtsChromeHost {
  @ObjectLink state: ChromeState;
  private labels: string[] = ['终端', '轨迹', '工具', '日志', '设置'];
  private icons: Resource[] = [$r('app.media.nav_terminal'), $r('app.media.nav_route'),
    $r('app.media.nav_tools'), $r('app.media.nav_logs'), $r('app.media.nav_settings')];
  aboutToAppear(): void {
    this.state.mounted = true;
    this.state.onMounted();
  }
  aboutToDisappear(): void {
    this.state.mounted = false;
    this.state.enabled = false;
  }
  private material(shadow: boolean = true): uiMaterial.ImmersiveMaterial {
    return new uiMaterial.ImmersiveMaterial({ style: uiMaterial.ImmersiveStyle.THIN,
      applyShadow: shadow, interactive: !this.state.reduceMotion,
      lightEffect: this.state.reduceMotion ? null : { color: '#B4FFF0' } });
  }
  @Builder
  header() {
    Row({ space: 12 }) {
      Image($r('app.media.cts_foreground')).width(36).height(36)
        .backgroundColor('#087F8C').borderRadius(8)
      Text(this.state.compact ? 'CTS' : 'CTS / Terminal Studio')
        .fontSize(`${14 * this.state.textScale}vp`).fontWeight(FontWeight.Bold)
        .fontColor(this.state.dark ? '#E5F0F2' : '#152B30')
        .maxLines(1).textOverflow({ overflow: TextOverflow.Ellipsis }).layoutWeight(1)
      Image(this.state.connected ? $r('app.media.chrome_link') : $r('app.media.chrome_link_off'))
        .width(18).height(18).fillColor(this.state.connected ? '#20C9A6' : '#819498')
      Text(this.state.status).fontSize(`${12 * this.state.textScale}vp`)
        .fontColor(this.state.dark ? '#C4D2D6' : '#46575C').maxLines(1)
    }.padding({ left: 16, right: 16 }).width('100%').height('100%')
      .borderRadius(this.state.cornerRadius)
      .backgroundColor(this.state.materialSupported ? Color.Transparent : (this.state.dark ? '#202D30' : '#F0F7F5'))
      // Custom title builders own their material surface inside the TitleBar scope.
      .systemMaterial(this.state.materialSupported ? this.material(false) : undefined)
      .shadow({ radius: 12, color: this.state.dark ? '#40000000' : '#18000000', offsetY: 4 })
      .hitTestBehavior(HitTestMode.BLOCK_HIERARCHY)
  }
  @Builder
  headerViewport() {
    Stack({ alignContent: Alignment.Center }) {
      this.header()
    }.width('100%').height('100%').padding(32)
      .hitTestBehavior(HitTestMode.Transparent)
  }
  @Builder
  tab(index: number) {
    Stack() {
      Column({ space: 4 }) {
        Image(this.icons[index]).width(24).height(24)
          .fillColor(this.state.selected === index ? '#20C9A6' : (this.state.dark ? '#C4D2D6' : '#46575C'))
        if (!this.state.iconOnly) {
          Text(this.labels[index]).fontSize(`${12 * this.state.textScale}vp`)
            .fontWeight(this.state.selected === index ? FontWeight.Bold : FontWeight.Medium)
            .fontColor(this.state.selected === index ? (this.state.dark ? '#A6F5E0' : '#006B61') : (this.state.dark ? '#C4D2D6' : '#46575C'))
            .maxLines(1)
        }
      }.width('100%').height('100%').justifyContent(FlexAlign.Center)
        .borderRadius(this.state.cornerRadius - this.state.contentInset)
        .backgroundColor(this.state.selected === index ? (this.state.dark ? '#403BCAB0' : '#3050BFA9') : Color.Transparent)
        .accessibilityText(this.labels[index])
        .accessibilityDescription(this.state.selected === index ? '已选中' : '')
    }.width('100%').height('100%').padding(this.state.contentInset)
  }
  build() {
    Stack({ alignContent: Alignment.TopStart }) {
      if (this.state.headerWidth > 0) {
        WithTheme({ colorMode: this.state.dark ? ThemeColorMode.DARK : ThemeColorMode.LIGHT }) {
          Navigation() {}
            .mode(NavigationMode.Stack).hideBackButton(true).hideToolBar(true)
            .title({ builder: () => this.headerViewport(), height: this.state.headerHeight + 64 },
              { backgroundColor: Color.Transparent, systemMaterial: uiMaterial.Material.empty,
                barStyle: BarStyle.STACK })
            .width(this.state.headerWidth + 64).height(this.state.headerHeight + 64)
            .position({ x: this.state.headerX - 32, y: this.state.headerY - 32 })
            .backgroundColor(Color.Transparent).clip(false)
            .focusable(false).hitTestBehavior(HitTestMode.Transparent)
            .visibility(this.state.enabled ? Visibility.Visible : Visibility.Hidden)
            .onAreaChange((oldArea: Area, area: Area) => this.state.markReady(true, area))
        }
      }
      if (this.state.bottomWidth > 0) {
        WithTheme({ colorMode: this.state.dark ? ThemeColorMode.DARK : ThemeColorMode.LIGHT }) {
          Tabs({ barPosition: BarPosition.End, index: this.state.selected,
            barModifier: new CommonModifier().borderRadius(this.state.cornerRadius) }) {
            ForEach([0, 1, 2, 3, 4], (index: number) => {
              TabContent() { Column().height(1).width('100%') }.tabBar(this.tab(index))
            })
          }.width(this.state.bottomWidth + 24).height(this.state.bottomHeight + 24)
            .position({ x: this.state.bottomX - 12, y: this.state.bottomY - 12 })
            .barHeight(this.state.bottomHeight).barMode(BarMode.Fixed)
            .vertical(false).scrollable(false).barOverlap(true).divider({ strokeWidth: 0 })
            .barBackgroundColor(this.state.materialSupported ? Color.Transparent : (this.state.dark ? '#202D30' : '#F0F7F5'))
            .barFloatingStyle({
              barWidth: { smallBarWidth: this.state.bottomWidth,
                mediumBarWidth: this.state.bottomWidth, largeBarWidth: this.state.bottomWidth },
              barSideMargin: 12, barBottomMargin: 12,
              maskHeight: 0, maskColor: Color.Transparent, systemMaterial: this.material() })
            .animationDuration(this.state.reduceMotion ? 0 : 220)
            .onTabBarClick((index: number) => this.state.select(index))
            .onChange((index: number) => this.state.select(index))
            .hitTestBehavior(HitTestMode.BLOCK_HIERARCHY)
            .visibility(this.state.enabled && this.state.bottomVisible ? Visibility.Visible : Visibility.Hidden)
            .onAreaChange((oldArea: Area, area: Area) => this.state.markReady(false, area))
        }
      }
    }.width('100%').height('100%').hitTestBehavior(HitTestMode.Transparent)
  }
}
