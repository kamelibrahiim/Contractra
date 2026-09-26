import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/language/locales.dart';
import 'package:contractra_employee/core/network/failures.dart';
import 'package:contractra_employee/core/session/session_cubit.dart';
import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_employee/module/auth/data_layer/models/login_result.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_employee/module/auth/login/cubit/login_cubit.dart';
import 'package:contractra_employee/module/auth/login/presentation/login_screen.dart';
import 'package:contractra_employee/module/auth/login/presentation/screens_layout/m_login_screen.dart';
import 'package:contractra_employee/module/auth/login/presentation/screens_layout/s_login_screen.dart';
import 'package:contractra_employee/utilities/git_it.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

const _user = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: false,
);

void main() {
  late _MockAuthRepository authRepository;
  late SessionCubit sessionCubit;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    // A cached asset future from a previous test's zone never completes here.
    rootBundle.clear();
    authRepository = _MockAuthRepository();
    sessionCubit = SessionCubit(authRepository);
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
          BlocProvider.value(value: sessionCubit),
          BlocProvider(create: (context) => LoginCubit(context.read<SessionCubit>())),
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

  Future<void> enterCredentials(WidgetTester tester, String identifier, String password) async {
    await tester.enterText(
      find.descendant(of: find.byKey(const Key('login_identifier')), matching: find.byType(EditableText)),
      identifier,
    );
    await tester.enterText(
      find.descendant(of: find.byKey(const Key('login_password')), matching: find.byType(EditableText)),
      password,
    );
  }

  group('Admin login screen', () {
    testWidgets('tablet width uses the centered layout with all elements', (tester) async {
      await pumpLogin(tester, const Size(768, 1024));

      expect(find.byType(MediumLoginScreen), findsOneWidget);
      expectAllElements();
      expect(tester.takeException(), isNull);
    });

    testWidgets('small width uses the single-column layout with all elements', (tester) async {
      await pumpLogin(tester, const Size(400, 800));

      expect(find.byType(SmallLoginScreen), findsOneWidget);
      expectAllElements();
      expect(tester.takeException(), isNull);
    });

    testWidgets('password is obscured and the toggle reveals it', (tester) async {
      await pumpLogin(tester, const Size(400, 800));
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
      await pumpLogin(tester, const Size(400, 800));

      await tester.tap(find.byKey(const Key('login_submit')));
      await tester.pump();

      expect(find.text('Enter both your identifier and password.'), findsOneWidget);
    });

    testWidgets('submitting valid credentials shows a loading button', (tester) async {
      when(() => authRepository.login('admin', 'Secret#2026')).thenAnswer(
        (_) => Future.delayed(
          const Duration(milliseconds: 300),
          () => const AuthSuccess(
            LoginResult(accessToken: 'a', refreshToken: 'r', mustChangePassword: false),
          ),
        ),
      );
      when(() => authRepository.me()).thenAnswer((_) async => const AuthSuccess(_user));
      await pumpLogin(tester, const Size(400, 800));

      await enterCredentials(tester, 'admin', 'Secret#2026');
      await tester.tap(find.byKey(const Key('login_submit')));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.text('Enter both your identifier and password.'), findsNothing);

      await tester.pump(const Duration(milliseconds: 400));
    });

    testWidgets('invalid credentials show one generic message, never naming the wrong field', (tester) async {
      when(() => authRepository.login('admin', 'wrong'))
          .thenAnswer((_) async => const AuthError(InvalidCredentialsFailure()));
      await pumpLogin(tester, const Size(400, 800));

      await enterCredentials(tester, 'admin', 'wrong');
      await tester.tap(find.byKey(const Key('login_submit')));
      await tester.pump();
      await tester.pump();

      expect(find.text('Incorrect identifier or password.'), findsOneWidget);
    });

    testWidgets('a disabled account shows its own message', (tester) async {
      when(() => authRepository.login('disabled.user', 'x'))
          .thenAnswer((_) async => const AuthError(UserDisabledFailure()));
      await pumpLogin(tester, const Size(400, 800));

      await enterCredentials(tester, 'disabled.user', 'x');
      await tester.tap(find.byKey(const Key('login_submit')));
      await tester.pump();
      await tester.pump();

      expect(find.text('This account has been disabled.'), findsOneWidget);
    });
  });
}
