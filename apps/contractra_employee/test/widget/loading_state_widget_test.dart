import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/widget/loading_state_widget.dart';
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

  group('LoadingStateWidget', () {
    testWidgets('renders a progress indicator with no label', (tester) async {
      await tester.pumpWidget(wrap(const LoadingStateWidget()));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('renders a progress indicator with a label', (tester) async {
      await tester.pumpWidget(wrap(const LoadingStateWidget(label: 'Loading…')));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Loading…'), findsOneWidget);
    });
  });
}
