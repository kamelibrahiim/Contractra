import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/core/theme/app_spacing.dart';

void main() {
  Future<void> pumpScreenUtil(WidgetTester tester) async {
    await tester.pumpWidget(
      ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => const SizedBox.shrink(),
      ),
    );
    await tester.pumpAndSettle();
  }

  group('AppSpacing', () {
    testWidgets('all spacing values resolve to positive values', (
      tester,
    ) async {
      await pumpScreenUtil(tester);

      expect(AppSpacing.xs, greaterThan(0));
      expect(AppSpacing.sm, greaterThan(0));
      expect(AppSpacing.md, greaterThan(0));
      expect(AppSpacing.lg, greaterThan(0));
      expect(AppSpacing.xl, greaterThan(0));
    });

    testWidgets(
      'spacing ordering is strictly ascending: xs < sm < md < lg < xl',
      (tester) async {
        await pumpScreenUtil(tester);

        expect(AppSpacing.xs, lessThan(AppSpacing.sm));
        expect(AppSpacing.sm, lessThan(AppSpacing.md));
        expect(AppSpacing.md, lessThan(AppSpacing.lg));
        expect(AppSpacing.lg, lessThan(AppSpacing.xl));
      },
    );
  });

  group('AppRadius', () {
    testWidgets('all radius values resolve to positive values', (tester) async {
      await pumpScreenUtil(tester);

      expect(AppRadius.sm, greaterThan(0));
      expect(AppRadius.md, greaterThan(0));
      expect(AppRadius.lg, greaterThan(0));
      expect(AppRadius.pill, greaterThan(0));
    });

    testWidgets('radius ordering is sensible: sm < md < lg', (tester) async {
      await pumpScreenUtil(tester);

      expect(AppRadius.sm, lessThan(AppRadius.md));
      expect(AppRadius.md, lessThan(AppRadius.lg));
    });

    testWidgets('pill is clearly larger than regular radii', (tester) async {
      await pumpScreenUtil(tester);

      expect(AppRadius.pill, greaterThan(AppRadius.lg));
      expect(AppRadius.pill, greaterThan(AppRadius.md));
      expect(AppRadius.pill, greaterThan(AppRadius.sm));
    });
  });
}
