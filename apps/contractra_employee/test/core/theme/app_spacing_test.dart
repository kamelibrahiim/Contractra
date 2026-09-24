import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

import 'package:contractra_employee/core/theme/app_spacing.dart';

void main() {
  Widget wrap(WidgetBuilder builder) => ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(home: Builder(builder: builder)),
      );

  group('AppSpacing', () {
    testWidgets('all spacing tokens are positive', (tester) async {
      await tester.pumpWidget(wrap((context) {
        for (final value in [
          AppSpacing.xs,
          AppSpacing.sm,
          AppSpacing.md,
          AppSpacing.lg,
          AppSpacing.xl,
          AppSpacing.xxl,
        ]) {
          expect(value, greaterThan(0));
        }
        return const SizedBox.shrink();
      }));
    });

    testWidgets('spacing scale increases logically: xs < sm < md < lg < xl < xxl',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(AppSpacing.xs, lessThan(AppSpacing.sm));
        expect(AppSpacing.sm, lessThan(AppSpacing.md));
        expect(AppSpacing.md, lessThan(AppSpacing.lg));
        expect(AppSpacing.lg, lessThan(AppSpacing.xl));
        expect(AppSpacing.xl, lessThan(AppSpacing.xxl));
        return const SizedBox.shrink();
      }));
    });

    testWidgets('tokens are responsive through ScreenUtilPlus (.w)', (tester) async {
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      double? smallMd;
      double? largeMd;

      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(720, 1280);
      await tester.pumpWidget(wrap((context) {
        smallMd = AppSpacing.md;
        return const SizedBox.shrink();
      }));
      await tester.pumpAndSettle();

      tester.view.physicalSize = const Size(1440, 2560);
      await tester.pumpWidget(wrap((context) {
        largeMd = AppSpacing.md;
        return const SizedBox.shrink();
      }));
      await tester.pumpAndSettle();

      expect(smallMd, isNotNull);
      expect(largeMd, isNotNull);
      expect(largeMd, greaterThan(smallMd!));
    });

    testWidgets('exposes usable EdgeInsets/gap helpers for widgets', (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(AppSpacing.allMd, isA<EdgeInsets>());
        expect(AppSpacing.allMd.left, AppSpacing.md);
        expect(AppSpacing.horizontalLg, isA<EdgeInsets>());
        expect(AppSpacing.horizontalLg.left, AppSpacing.lg);
        expect(AppSpacing.horizontalLg.top, 0);
        expect(AppSpacing.verticalSm, isA<EdgeInsets>());
        expect(AppSpacing.verticalSm.top, AppSpacing.sm);
        expect(AppSpacing.verticalSm.left, 0);
        return const SizedBox.shrink();
      }));
    });
  });
}
