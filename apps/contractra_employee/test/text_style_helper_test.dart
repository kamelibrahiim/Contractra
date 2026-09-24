import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

import 'package:contractra_employee/utilities/text_style_helper.dart';

void main() {
  Widget wrap(WidgetBuilder builder) => ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(home: Builder(builder: builder)),
      );

  group('TextStyleHelper', () {
    testWidgets('every typography role returns a valid TextStyle',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        final styles = <TextStyle>[
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

        for (final style in styles) {
          expect(style.fontSize, isNotNull);
          expect(style.fontSize, greaterThan(0));
          expect(style.fontWeight, isNotNull);
        }

        return const SizedBox.shrink();
      }));
    });

    testWidgets('heading hierarchy decreases logically: h1 > h2 > h3 > h4',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(TextStyleHelper.h1.fontSize!, greaterThan(TextStyleHelper.h2.fontSize!));
        expect(TextStyleHelper.h2.fontSize!, greaterThan(TextStyleHelper.h3.fontSize!));
        expect(TextStyleHelper.h3.fontSize!, greaterThan(TextStyleHelper.h4.fontSize!));
        return const SizedBox.shrink();
      }));
    });

    testWidgets('body hierarchy decreases logically: bodyLg > bodyMd > bodySm',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        expect(TextStyleHelper.bodyLg.fontSize!,
            greaterThan(TextStyleHelper.bodyMd.fontSize!));
        expect(TextStyleHelper.bodyMd.fontSize!,
            greaterThan(TextStyleHelper.bodySm.fontSize!));
        return const SizedBox.shrink();
      }));
    });

    testWidgets('headings use bolder weights than body text, and no role is thin',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        final minHeadingWeight = [
          TextStyleHelper.h1,
          TextStyleHelper.h2,
          TextStyleHelper.h3,
          TextStyleHelper.h4,
        ].map((s) => s.fontWeight!.value).reduce((a, b) => a < b ? a : b);

        final maxBodyWeight = [
          TextStyleHelper.bodyLg,
          TextStyleHelper.bodyMd,
          TextStyleHelper.bodySm,
        ].map((s) => s.fontWeight!.value).reduce((a, b) => a > b ? a : b);

        expect(minHeadingWeight, greaterThanOrEqualTo(maxBodyWeight));

        final allRoles = <TextStyle>[
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
        for (final style in allRoles) {
          expect(style.fontWeight!.value, greaterThanOrEqualTo(FontWeight.w400.value));
        }

        expect(TextStyleHelper.label.fontWeight!.value,
            greaterThanOrEqualTo(FontWeight.w500.value));

        return const SizedBox.shrink();
      }));
    });

    testWidgets('supports overriding style properties without rebuilding manually',
        (tester) async {
      await tester.pumpWidget(wrap((context) {
        const overrideColor = Color(0xFF2F6E4E);
        final overridden = TextStyleHelper.bodyMd.copyWith(color: overrideColor);

        expect(overridden.color, overrideColor);
        expect(overridden.fontSize, TextStyleHelper.bodyMd.fontSize);
        expect(overridden.fontWeight, TextStyleHelper.bodyMd.fontWeight);

        return const SizedBox.shrink();
      }));
    });

    testWidgets('typography scales responsively via ScreenUtilPlus (.sp)',
        (tester) async {
      final originalSize = tester.view.physicalSize;
      final originalRatio = tester.view.devicePixelRatio;
      addTearDown(() {
        tester.view.resetPhysicalSize();
        tester.view.resetDevicePixelRatio();
      });

      TextStyle? smallScreenH1;
      TextStyle? largeScreenH1;

      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(720, 1280);
      await tester.pumpWidget(ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(
          home: Builder(builder: (context) {
            smallScreenH1 = TextStyleHelper.h1;
            return const SizedBox.shrink();
          }),
        ),
      ));
      await tester.pumpAndSettle();

      tester.view.physicalSize = const Size(1440, 2560);
      await tester.pumpWidget(ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(
          home: Builder(builder: (context) {
            largeScreenH1 = TextStyleHelper.h1;
            return const SizedBox.shrink();
          }),
        ),
      ));
      await tester.pumpAndSettle();

      expect(smallScreenH1, isNotNull);
      expect(largeScreenH1, isNotNull);
      expect(largeScreenH1!.fontSize, greaterThan(smallScreenH1!.fontSize!));

      tester.view.physicalSize = originalSize;
      tester.view.devicePixelRatio = originalRatio;
    });
  });
}
