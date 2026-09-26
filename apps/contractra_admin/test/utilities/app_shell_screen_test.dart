import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:mocktail/mocktail.dart';

import 'package:contractra_admin/core/language/locales.dart';
import 'package:contractra_admin/core/session/session_cubit.dart';
import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_admin/utilities/app_shell_screen.dart';
import 'package:contractra_admin/utilities/git_it.dart';
import 'package:contractra_admin/widget/nav_rail_item.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

void main() {
  late SessionCubit sessionCubit;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    // A cached asset future from a previous test's zone never completes here.
    rootBundle.clear();
    final authRepository = _MockAuthRepository();
    when(() => authRepository.logout()).thenAnswer((_) async {});
    sessionCubit = SessionCubit(authRepository);
  });

  GoRouter buildRouter(String initial) => GoRouter(
        initialLocation: initial,
        routes: [
          StatefulShellRoute.indexedStack(
            builder: (_, _, shell) => AppShellScreen(navigationShell: shell),
            branches: [
              for (final d in adminShellDestinations)
                StatefulShellBranch(routes: [
                  GoRoute(
                    path: d.path,
                    builder: (_, _) => Center(child: Text('page:${d.path}')),
                  ),
                ]),
            ],
          ),
        ],
      );

  Future<void> pumpShell(WidgetTester tester, Size size, {String initial = '/dashboard'}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => MultiBlocProvider(
        providers: [
          BlocProvider(create: (_) => ThemeCubit()),
          BlocProvider.value(value: sessionCubit),
        ],
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
    // The i18n delegate reads JSON assets with real async I/O.
    for (var i = 0; i < 100 && find.byType(AppShellScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  group('AppShellScreen', () {
    testWidgets('large width shows the rail with all 5 modules and no menu icon', (tester) async {
      await pumpShell(tester, const Size(1400, 900));

      expect(find.byType(NavRailItem), findsNWidgets(6));
      expect(find.byIcon(Icons.menu), findsNothing);
      for (final label in ['Dashboard', 'Parties', 'Operations', 'Approvals', 'Reports']) {
        expect(find.text(label), findsOneWidget);
      }
      expect(tester.takeException(), isNull);
    });

    testWidgets('selecting a rail entry navigates to its module', (tester) async {
      await pumpShell(tester, const Size(1400, 900));

      await tester.tap(find.text('Reports'));
      await tester.pumpAndSettle();

      expect(find.text('page:/reports'), findsOneWidget);
    });

    for (final size in const [Size(800, 900), Size(400, 800)]) {
      testWidgets('width ${size.width} collapses to a drawer that navigates', (tester) async {
        await pumpShell(tester, size);

        expect(find.byType(NavRailItem), findsNothing);
        await tester.tap(find.byIcon(Icons.menu));
        await tester.pumpAndSettle();
        expect(find.byType(Drawer), findsOneWidget);

        await tester.tap(find.text('Parties'));
        await tester.pumpAndSettle();

        expect(find.byType(Drawer), findsNothing);
        expect(find.text('page:/parties'), findsOneWidget);
        expect(tester.takeException(), isNull);
      });
    }

    testWidgets('deep link opens the matching module directly', (tester) async {
      await pumpShell(tester, const Size(1400, 900), initial: '/approvals');

      expect(find.text('page:/approvals'), findsOneWidget);
    });

    testWidgets('tapping logout on the rail calls SessionCubit.logout', (tester) async {
      await pumpShell(tester, const Size(1400, 900));

      await tester.tap(find.byKey(const Key('shell_logout')));
      await tester.pump();

      expect(sessionCubit.state, const SessionUnauthenticated());
    });

    testWidgets('tapping logout on the app bar (narrow width) calls SessionCubit.logout', (tester) async {
      await pumpShell(tester, const Size(400, 800));

      await tester.tap(find.byKey(const Key('shell_logout')));
      await tester.pump();

      expect(sessionCubit.state, const SessionUnauthenticated());
    });
  });
}
