import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/widget/empty_state_widget.dart';
import 'package:contractra_employee/utilities/git_it.dart';

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

  group('EmptyStateWidget', () {
    testWidgets('renders with only a title', (tester) async {
      await tester.pumpWidget(wrap(const EmptyStateWidget(title: 'No items yet')));

      expect(find.text('No items yet'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('renders with title and message', (tester) async {
      await tester.pumpWidget(wrap(const EmptyStateWidget(
        title: 'No items yet',
        message: 'Try adjusting your filters.',
      )));

      expect(find.text('No items yet'), findsOneWidget);
      expect(find.text('Try adjusting your filters.'), findsOneWidget);
    });

    testWidgets('renders action button when actionLabel and onAction provided',
        (tester) async {
      var tapped = false;
      await tester.pumpWidget(wrap(EmptyStateWidget(
        title: 'No items yet',
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
      await tester.pumpWidget(wrap(const EmptyStateWidget(
        title: 'No items yet',
        actionLabel: 'Retry',
      )));

      expect(find.text('Retry'), findsNothing);
    });
  });
}
