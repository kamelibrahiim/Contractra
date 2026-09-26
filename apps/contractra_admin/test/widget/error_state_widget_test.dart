import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/widget/error_state_widget.dart';
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
            child: Scaffold(body: child),
          ),
        ),
      );

  group('ErrorStateWidget', () {
    testWidgets('renders with only a title', (tester) async {
      await tester.pumpWidget(wrap(const ErrorStateWidget(title: 'Something went wrong')));

      expect(find.text('Something went wrong'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('renders with title and message', (tester) async {
      await tester.pumpWidget(wrap(const ErrorStateWidget(
        title: 'Something went wrong',
        message: 'Please try again later.',
      )));

      expect(find.text('Something went wrong'), findsOneWidget);
      expect(find.text('Please try again later.'), findsOneWidget);
    });

    testWidgets('renders action button when actionLabel and onAction provided',
        (tester) async {
      var tapped = false;
      await tester.pumpWidget(wrap(ErrorStateWidget(
        title: 'Something went wrong',
        actionLabel: 'Retry',
        onAction: () => tapped = true,
      )));

      expect(find.text('Retry'), findsOneWidget);
      await tester.tap(find.text('Retry'));
      await tester.pump();
      expect(tapped, isTrue);
    });

    testWidgets('does not render action button when callback missing',
        (tester) async {
      await tester.pumpWidget(wrap(const ErrorStateWidget(
        title: 'Something went wrong',
        actionLabel: 'Retry',
      )));

      expect(find.text('Retry'), findsNothing);
    });
  });
}
