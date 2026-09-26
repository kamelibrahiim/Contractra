import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/widget/app_button.dart';
import 'package:contractra_admin/utilities/git_it.dart';

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
  });

  Widget wrap(Widget child) => ScreenUtilPlusInit(
        designSize: const Size(1440, 1088),
        builder: (_, __) => MaterialApp(
          home: BlocProvider(
            create: (_) => ThemeCubit(),
            child: Scaffold(body: Center(child: child)),
          ),
        ),
      );

  group('AppButton', () {
    testWidgets('renders the label', (tester) async {
      await tester.pumpWidget(wrap(AppButton(label: 'Submit', onPressed: () {})));

      expect(find.text('Submit'), findsOneWidget);
    });

    testWidgets('enabled button invokes callback when tapped', (tester) async {
      var tapped = false;
      await tester.pumpWidget(
        wrap(AppButton(label: 'Submit', onPressed: () => tapped = true)),
      );

      await tester.tap(find.byType(AppButton));
      await tester.pump();

      expect(tapped, isTrue);
    });

    testWidgets('disabled button (onPressed null) does not invoke callback',
        (tester) async {
      await tester.pumpWidget(wrap(const AppButton(label: 'Submit', onPressed: null)));

      await tester.tap(find.byType(AppButton), warnIfMissed: false);
      await tester.pump();

      expect(find.text('Submit'), findsOneWidget);
    });

    testWidgets('loading button does not invoke callback', (tester) async {
      var tapped = false;
      await tester.pumpWidget(
        wrap(AppButton(
          label: 'Submit',
          onPressed: () => tapped = true,
          isLoading: true,
        )),
      );

      await tester.tap(find.byType(AppButton), warnIfMissed: false);
      await tester.pump();

      expect(tapped, isFalse);
    });

    testWidgets('loading state shows a progress indicator and hides the label',
        (tester) async {
      await tester.pumpWidget(
        wrap(AppButton(label: 'Submit', onPressed: () {}, isLoading: true)),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Submit'), findsNothing);
    });

    testWidgets('primary variant renders successfully', (tester) async {
      await tester.pumpWidget(wrap(AppButton(
        label: 'Primary',
        onPressed: () {},
        variant: AppButtonVariant.primary,
      )));

      expect(find.byType(AppButton), findsOneWidget);
    });

    testWidgets('secondary variant renders successfully', (tester) async {
      await tester.pumpWidget(wrap(AppButton(
        label: 'Secondary',
        onPressed: () {},
        variant: AppButtonVariant.secondary,
      )));

      expect(find.byType(AppButton), findsOneWidget);
    });

    testWidgets('text variant renders successfully', (tester) async {
      await tester.pumpWidget(wrap(AppButton(
        label: 'Cancel',
        onPressed: () {},
        variant: AppButtonVariant.text,
      )));

      expect(find.byType(AppButton), findsOneWidget);
    });

    testWidgets('optional leading icon renders', (tester) async {
      await tester.pumpWidget(wrap(AppButton(
        label: 'Save',
        onPressed: () {},
        leadingIcon: Icons.save,
      )));

      expect(find.byIcon(Icons.save), findsOneWidget);
    });

    testWidgets('optional trailing icon renders', (tester) async {
      await tester.pumpWidget(wrap(AppButton(
        label: 'Next',
        onPressed: () {},
        trailingIcon: Icons.arrow_forward,
      )));

      expect(find.byIcon(Icons.arrow_forward), findsOneWidget);
    });

    testWidgets('full-width mode occupies available horizontal space',
        (tester) async {
      await tester.pumpWidget(wrap(SizedBox(
        width: 300,
        child: AppButton(label: 'Submit', onPressed: () {}, isFullWidth: true),
      )));

      final buttonSize = tester.getSize(find.byType(AppButton));
      expect(buttonSize.width, 300);
    });

    testWidgets('default (non full-width) mode does not force max width',
        (tester) async {
      await tester.pumpWidget(wrap(SizedBox(
        width: 300,
        child: Align(
          alignment: Alignment.center,
          child: AppButton(label: 'OK', onPressed: () {}),
        ),
      )));

      final buttonSize = tester.getSize(find.byType(AppButton));
      expect(buttonSize.width, lessThan(300));
    });

    testWidgets('minimum interactive height is at least 44 logical pixels',
        (tester) async {
      await tester.pumpWidget(wrap(AppButton(label: 'Submit', onPressed: () {})));

      final buttonSize = tester.getSize(find.byType(AppButton));
      expect(buttonSize.height, greaterThanOrEqualTo(44));
    });

    testWidgets(
        'loading state does not show label or icons alongside the indicator',
        (tester) async {
      await tester.pumpWidget(wrap(AppButton(
        label: 'Submit',
        onPressed: () {},
        leadingIcon: Icons.save,
        trailingIcon: Icons.arrow_forward,
        isLoading: true,
      )));

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Submit'), findsNothing);
      expect(find.byIcon(Icons.save), findsNothing);
      expect(find.byIcon(Icons.arrow_forward), findsNothing);
    });
  });
}
