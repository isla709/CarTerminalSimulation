import 'package:flutter/widgets.dart';

/// Concentric corners for the shell's floating surfaces and inset controls.
abstract final class StudioGeometry {
  static const floatingRadius = 28.0;
  static const navigationInset = 6.0;
  static const navigationItemRadius = floatingRadius - navigationInset;
  static const floatingCorners = BorderRadius.all(
    Radius.circular(floatingRadius),
  );
  static const navigationItemCorners = BorderRadius.all(
    Radius.circular(navigationItemRadius),
  );
}
