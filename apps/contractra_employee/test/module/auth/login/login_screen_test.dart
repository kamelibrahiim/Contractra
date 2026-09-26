import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/language/locales.dart';
import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/module/auth/login/cubit/login_cubit.dart';
import 'package:contractra_employee/module/auth/login/presentation/login_screen.dart';
import 'package:contractra_employee/module/auth/login/presentation/screens_layout/m_login_screen.dart';
import 'package:contractra_employee/module/auth/login/presentation/screens_layout/s_login_screen.dart';
import 'package:contractra_employee/utilities/git_it.dart';

void main() {
  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    // A cached asset future from a previous test's zone never completes here.
    rootBundle.clear();
  });

  Future<void> pumpLogin(WidgetTester tester, Size size) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => MultiBlocProvider(
        providers: [
          BlocProvider(create: (_) => ThemeCubit()),
          BlocProvider(
            create: (_) => LoginCubit(simulatedDelay: const Duration(milliseconds: 300)),
          ),
        ],
        child: const MaterialApp(
          locale: Locale('en'),
          localizationsDelegates: [
            AppLocalizationsDelegate(),
            DefaultMaterialLocalizations.delegate,
            DefaultWidgetsLocalizations.delegate,
          ],
          home: LoginView(),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(LoginView).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  void expectAllElements() {
    expect(find.byKey(const Key('login_identifier')), findsOneWidget);
    expect(find.byKey(const Key('login_password')), findsOneWidget);
    expect(find.byKey(const Key('login_submit')), findsOneWidget);
    final forgot = find.byKey(const Key('login_forgot_password'));
    expect(forgot, findsOneWidget);
    expect((forgot.evaluate().single.widget as TextButton).onPressed, isNull);
  }

  group('Employee login screen', () {
    testWidgets('tablet width uses the centered medium layout with all elements', (tester) async {
      await pumpLogin(tester, const Size(768, 1024));

      expect(find.byType(MediumLoginScreen), findsOneWidget);
      expectAllElements();
      expect(tester.takeException(), isNull);
    });

    testWidgets('small width uses the single-column layout with all elements', (tester) async {
      await pumpLogin(tester, const Size(375, 812));

      expect(find.byType(SmallLoginScreen), findsOneWidget);
      expectAllElements();
      expect(tester.takeException(), isNull);
    });

    testWidgets('password is obscured and the toggle reveals it', (tester) async {
      await pumpLogin(tester, const Size(375, 812));
      bool obscured() => tester
          .widget<EditableText>(find.descendant(
            of: find.byKey(const Key('login_password')),
            matching: find.byType(EditableText),
          ))
          .obscureText;

      expect(obscured(), isTrue);
      await tester.tap(find.byIcon(Icons.visibility_outlined));
      await tester.pump();
      expect(obscured(), isFalse);
    });

    testWidgets('submitting empty fields shows the local validation message', (tester) async {
      await pumpLogin(tester, const Size(375, 812));

      await tester.tap(find.byKey(const Key('login_submit')));
      await tester.pump();

      expect(find.text('Enter both your identifier and password.'), findsOneWidget);
    });

    testWidgets('submitting filled fields shows loading then returns to idle', (tester) async {
      await pumpLogin(tester, const Size(375, 812));

      await tester.enterText(
        find.descendant(of: find.byKey(const Key('login_identifier')), matching: find.byType(EditableText)),
        'admin',
      );
      await tester.enterText(
        find.descendant(of: find.byKey(const Key('login_password')), matching: find.byType(EditableText)),
        'secret',
      );
      await tester.tap(find.byKey(const Key('login_submit')));
      await tester.pump();
      expect(find.byType(CircularProgressIndicator), findsOneWidget);

      await tester.pump(const Duration(milliseconds: 400));
      expect(find.byType(CircularProgressIndicator), findsNothing);
      expect(find.text('Enter both your identifier and password.'), findsNothing);
    });
  });
}
