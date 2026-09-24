import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/utilities/text_style_helper.dart';

void main() {
  Future<TextStyleHelper> pumpHelper(WidgetTester tester) async {
    late TextStyleHelper helper;
    await tester.pumpWidget(
      ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => Builder(
          builder: (context) {
            helper = TextStyleHelper.of(context);
            return const SizedBox.shrink();
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
    return helper;
  }

  group('TextStyleHelper', () {
    testWidgets('exposes all required typography roles as non-null styles', (
      tester,
    ) async {
      final helper = await pumpHelper(tester);

      expect(helper.h1, isNotNull);
      expect(helper.h2, isNotNull);
      expect(helper.h3, isNotNull);
      expect(helper.h4, isNotNull);
      expect(helper.bodyLg, isNotNull);
      expect(helper.bodyMd, isNotNull);
      expect(helper.bodySm, isNotNull);
      expect(helper.caption, isNotNull);
      expect(helper.label, isNotNull);
    });

    testWidgets('heading sizes are strictly descending: h1 > h2 > h3 > h4', (
      tester,
    ) async {
      final helper = await pumpHelper(tester);

      expect(helper.h1.fontSize!, greaterThan(helper.h2.fontSize!));
      expect(helper.h2.fontSize!, greaterThan(helper.h3.fontSize!));
      expect(helper.h3.fontSize!, greaterThan(helper.h4.fontSize!));
    });

    testWidgets(
      'body hierarchy is non-increasing: bodyLg >= bodyMd >= bodySm',
      (tester) async {
        final helper = await pumpHelper(tester);

        expect(
          helper.bodyLg.fontSize!,
          greaterThanOrEqualTo(helper.bodyMd.fontSize!),
        );
        expect(
          helper.bodyMd.fontSize!,
          greaterThanOrEqualTo(helper.bodySm.fontSize!),
        );
      },
    );

    testWidgets('every role explicitly defines fontWeight', (tester) async {
      final helper = await pumpHelper(tester);

      for (final style in [
        helper.h1,
        helper.h2,
        helper.h3,
        helper.h4,
        helper.bodyLg,
        helper.bodyMd,
        helper.bodySm,
        helper.caption,
        helper.label,
      ]) {
        expect(style.fontWeight, isNotNull);
      }
    });

    testWidgets('every role explicitly defines height', (tester) async {
      final helper = await pumpHelper(tester);

      for (final style in [
        helper.h1,
        helper.h2,
        helper.h3,
        helper.h4,
        helper.bodyLg,
        helper.bodyMd,
        helper.bodySm,
        helper.caption,
        helper.label,
      ]) {
        expect(style.height, isNotNull);
      }
    });

    testWidgets('no role bakes in a color', (tester) async {
      final helper = await pumpHelper(tester);

      for (final style in [
        helper.h1,
        helper.h2,
        helper.h3,
        helper.h4,
        helper.bodyLg,
        helper.bodyMd,
        helper.bodySm,
        helper.caption,
        helper.label,
      ]) {
        expect(style.color, isNull);
      }
    });

    testWidgets('responsive font sizes are valid positive values inside a '
        'ScreenUtil-enabled widget test', (tester) async {
      final helper = await pumpHelper(tester);

      for (final style in [
        helper.h1,
        helper.h2,
        helper.h3,
        helper.h4,
        helper.bodyLg,
        helper.bodyMd,
        helper.bodySm,
        helper.caption,
        helper.label,
      ]) {
        expect(style.fontSize, isNotNull);
        expect(style.fontSize!, greaterThan(0));
      }
    });
  });
}
