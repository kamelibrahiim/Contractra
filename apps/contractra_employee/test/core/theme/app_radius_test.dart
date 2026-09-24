import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

import 'package:contractra_employee/core/theme/app_radius.dart';

void main() {
  Widget wrap(WidgetBuilder builder) => ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(home: Builder(builder: builder)),
      );

  group('AppRadius', () {
    testWidgets('radius scale increases logically: sm < md < lg < xl', (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(AppRadius.sm, lessThan(AppRadius.md));
        expect(AppRadius.md, lessThan(AppRadius.lg));
        expect(AppRadius.lg, lessThan(AppRadius.xl));
        return const SizedBox.shrink();
      }));
    });

    testWidgets('pill is fully rounded and at least as large as xl', (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(AppRadius.pill, greaterThanOrEqualTo(AppRadius.xl));

        const box = Size(48, 48);
        final borderRadius = AppRadius.pillRadius;
        expect(borderRadius.topLeft.x, greaterThanOrEqualTo(box.shortestSide / 2));
        return const SizedBox.shrink();
      }));
    });

    testWidgets('exposes BorderRadius helpers matching the scalar scale',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(AppRadius.mdRadius, isA<BorderRadius>());
        expect(AppRadius.mdRadius.topLeft.x, AppRadius.md);
        expect(AppRadius.lgRadius.topLeft.x, AppRadius.lg);
        return const SizedBox.shrink();
      }));
    });

    testWidgets('radius tokens are responsive through ScreenUtilPlus (.r)',
        (tester) async {
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      double? smallMd;
      double? largeMd;

      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(720, 1280);
      await tester.pumpWidget(wrap((context) {
        smallMd = AppRadius.md;
        return const SizedBox.shrink();
      }));
      await tester.pumpAndSettle();

      tester.view.physicalSize = const Size(1440, 2560);
      await tester.pumpWidget(wrap((context) {
        largeMd = AppRadius.md;
        return const SizedBox.shrink();
      }));
      await tester.pumpAndSettle();

      expect(smallMd, isNotNull);
      expect(largeMd, isNotNull);
      expect(largeMd, greaterThan(smallMd!));
    });
  });
}
