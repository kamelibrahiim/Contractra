import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/utilities/text_style_helper.dart';

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

  List<TextStyle> allRoles() => [
    TextStyleHelper.h1,
    TextStyleHelper.h2,
    TextStyleHelper.h3,
    TextStyleHelper.h4,
    TextStyleHelper.bodyLg,
    TextStyleHelper.bodyMd,
    TextStyleHelper.bodySm,
    TextStyleHelper.caption,
    TextStyleHelper.label,
  ];

  group('TextStyleHelper', () {
    testWidgets('exposes all required typography roles as non-null styles', (
      tester,
    ) async {
      await pumpScreenUtil(tester);

      for (final style in allRoles()) {
        expect(style, isNotNull);
      }
    });

    testWidgets('heading sizes are strictly descending: h1 > h2 > h3 > h4', (
      tester,
    ) async {
      await pumpScreenUtil(tester);

      expect(
        TextStyleHelper.h1.fontSize!,
        greaterThan(TextStyleHelper.h2.fontSize!),
      );
      expect(
        TextStyleHelper.h2.fontSize!,
        greaterThan(TextStyleHelper.h3.fontSize!),
      );
      expect(
        TextStyleHelper.h3.fontSize!,
        greaterThan(TextStyleHelper.h4.fontSize!),
      );
    });

    testWidgets(
      'body hierarchy is non-increasing: bodyLg >= bodyMd >= bodySm',
      (tester) async {
        await pumpScreenUtil(tester);

        expect(
          TextStyleHelper.bodyLg.fontSize!,
          greaterThanOrEqualTo(TextStyleHelper.bodyMd.fontSize!),
        );
        expect(
          TextStyleHelper.bodyMd.fontSize!,
          greaterThanOrEqualTo(TextStyleHelper.bodySm.fontSize!),
        );
      },
    );

    testWidgets('every role explicitly defines fontWeight', (tester) async {
      await pumpScreenUtil(tester);

      for (final style in allRoles()) {
        expect(style.fontWeight, isNotNull);
      }
    });

    testWidgets('every role explicitly defines height', (tester) async {
      await pumpScreenUtil(tester);

      for (final style in allRoles()) {
        expect(style.height, isNotNull);
      }
    });

    testWidgets('no role bakes in a color', (tester) async {
      await pumpScreenUtil(tester);

      for (final style in allRoles()) {
        expect(style.color, isNull);
      }
    });

    testWidgets('responsive font sizes are valid positive values inside a '
        'ScreenUtil-enabled widget test', (tester) async {
      await pumpScreenUtil(tester);

      for (final style in allRoles()) {
        expect(style.fontSize, isNotNull);
        expect(style.fontSize!, greaterThan(0));
      }
    });
  });
}
