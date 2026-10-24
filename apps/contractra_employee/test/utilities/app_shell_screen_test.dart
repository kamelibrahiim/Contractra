import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/language/locales.dart';
import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/utilities/app_shell_screen.dart';
import 'package:contractra_employee/utilities/git_it.dart';

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    // A cached asset future from a previous test's zone never completes here.
    rootBundle.clear();
  });

  // Each tab gets a dummy "detail" sub-route to verify per-tab stack preservation.
  GoRouter buildRouter(String initial) => GoRouter(
        initialLocation: initial,
        routes: [
          StatefulShellRoute.indexedStack(
            builder: (_, _, shell) => AppShellScreen(navigationShell: shell),
            branches: [
              for (final d in employeeShellDestinations)
                StatefulShellBranch(routes: [
                  GoRoute(
                    path: d.path,
                    builder: (context, _) => Center(
                      child: TextButton(
                        onPressed: () => context.go('${d.path}/detail'),
                        child: Text('page:${d.path}'),
                      ),
                    ),
                    routes: [
                      GoRoute(
                        path: 'detail',
                        builder: (_, _) => Center(child: Text('detail:${d.path}')),
                      ),
                    ],
                  ),
                ]),
            ],
          ),
        ],
      );

  Future<void> pumpShell(WidgetTester tester, Size size, {String initial = '/assignments'}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => BlocProvider(
        create: (_) => ThemeCubit(),
        child: MaterialApp.router(
          locale: const Locale('en'),
          localizationsDelegates: const [
            AppLocalizationsDelegate(),
            DefaultMaterialLocalizations.delegate,
            DefaultWidgetsLocalizations.delegate,
          ],
          routerConfig: buildRouter(initial),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(AppShellScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  group('AppShellScreen', () {
    for (final size in const [Size(375, 812), Size(768, 1024)]) {
      testWidgets('shows 3 engineer tabs at width ${size.width} without overflow', (tester) async {
        await pumpShell(tester, size);

        expect(find.byType(NavigationBar), findsOneWidget);
        expect(find.byType(NavigationDestination), findsNWidgets(3));
        for (final label in ['My Sites', 'Expenses', 'Profile']) {
          expect(find.text(label), findsOneWidget);
        }
        expect(tester.takeException(), isNull);
      });
    }

    testWidgets('tapping a tab shows its screen', (tester) async {
      await pumpShell(tester, const Size(375, 812));

      await tester.tap(find.text('Profile'));
      await tester.pumpAndSettle();

      expect(find.text('page:/profile'), findsOneWidget);
    });

    testWidgets('switching tabs preserves each tab\'s own stack', (tester) async {
      await pumpShell(tester, const Size(375, 812));

      await tester.tap(find.text('page:/assignments'));
      await tester.pumpAndSettle();
      expect(find.text('detail:/assignments'), findsOneWidget);

      await tester.tap(find.text('Expenses'));
      await tester.pumpAndSettle();
      expect(find.text('page:/expenses'), findsOneWidget);

      await tester.tap(find.text('My Sites'));
      await tester.pumpAndSettle();
      expect(find.text('detail:/assignments'), findsOneWidget);
    });

    testWidgets('direct route opens the matching tab', (tester) async {
      await pumpShell(tester, const Size(375, 812), initial: '/expenses');

      expect(find.text('page:/expenses'), findsOneWidget);
    });
  });
}
