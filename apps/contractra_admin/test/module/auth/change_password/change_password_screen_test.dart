import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/language/locales.dart';
import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/core/session/session_cubit.dart';
import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/module/auth/change_password/presentation/change_password_screen.dart';
import 'package:contractra_admin/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_admin/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_admin/utilities/git_it.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

const _userMustChange = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: true,
);

void main() {
  late _MockAuthRepository authRepository;
  late SessionCubit sessionCubit;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    rootBundle.clear();
    authRepository = _MockAuthRepository();
    sessionCubit = SessionCubit(authRepository)
      ..emit(const SessionPasswordChangeRequired(_userMustChange));
    GitIt.instance.registerLazySingleton<AuthRepository>(() => authRepository);
  });

  Future<void> pumpScreen(WidgetTester tester) async {
    await tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => MultiBlocProvider(
        providers: [
          BlocProvider(create: (_) => ThemeCubit()),
          BlocProvider.value(value: sessionCubit),
        ],
        child: const MaterialApp(
          locale: Locale('en'),
          localizationsDelegates: [
            AppLocalizationsDelegate(),
            DefaultMaterialLocalizations.delegate,
            DefaultWidgetsLocalizations.delegate,
          ],
          home: ChangePasswordScreen(),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(ChangePasswordScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  Future<void> fillAndSubmit(
    WidgetTester tester, {
    required String current,
    required String newPassword,
    required String confirm,
  }) async {
    await tester.enterText(
      find.descendant(of: find.byKey(const Key('change_password_current')), matching: find.byType(EditableText)),
      current,
    );
    await tester.enterText(
      find.descendant(of: find.byKey(const Key('change_password_new')), matching: find.byType(EditableText)),
      newPassword,
    );
    await tester.enterText(
      find.descendant(of: find.byKey(const Key('change_password_confirm')), matching: find.byType(EditableText)),
      confirm,
    );
    await tester.tap(find.byKey(const Key('change_password_submit')));
    await tester.pump();
  }

  group('ChangePasswordScreen', () {
    testWidgets('renders all 3 fields and the submit button', (tester) async {
      await pumpScreen(tester);

      expect(find.byKey(const Key('change_password_current')), findsOneWidget);
      expect(find.byKey(const Key('change_password_new')), findsOneWidget);
      expect(find.byKey(const Key('change_password_confirm')), findsOneWidget);
      expect(find.byKey(const Key('change_password_submit')), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('too-short new password shows the client-side message', (tester) async {
      await pumpScreen(tester);
      await fillAndSubmit(tester, current: '12345678', newPassword: 'short1', confirm: 'short1');

      expect(find.text('Password must be at least 8 characters.'), findsOneWidget);
    });

    testWidgets('the default password is rejected as the new password', (tester) async {
      await pumpScreen(tester);
      await fillAndSubmit(tester, current: '12345678', newPassword: '12345678', confirm: '12345678');

      expect(find.text('Choose a password other than the default one.'), findsOneWidget);
    });

    testWidgets('a mismatched confirm shows the mismatch message', (tester) async {
      await pumpScreen(tester);
      await fillAndSubmit(
        tester,
        current: '12345678',
        newPassword: 'NewPass#2026',
        confirm: 'Different#2026',
      );

      expect(find.text('Passwords do not match.'), findsOneWidget);
    });

    testWidgets('a server error is shown', (tester) async {
      when(() => authRepository.changePassword(currentPassword: 'wrong', newPassword: 'NewPass#2026'))
          .thenAnswer((_) async => const AuthError(InvalidCredentialsFailure()));
      await pumpScreen(tester);

      await fillAndSubmit(tester, current: 'wrong', newPassword: 'NewPass#2026', confirm: 'NewPass#2026');
      await tester.pump();

      expect(find.text('Current password is incorrect.'), findsOneWidget);
    });

    testWidgets('success moves the session to authenticated', (tester) async {
      when(() => authRepository.changePassword(currentPassword: '12345678', newPassword: 'NewPass#2026'))
          .thenAnswer((_) async => const AuthSuccess(null));
      await pumpScreen(tester);

      await fillAndSubmit(tester, current: '12345678', newPassword: 'NewPass#2026', confirm: 'NewPass#2026');
      await tester.pump();

      expect(sessionCubit.state, isA<SessionAuthenticated>());
    });
  });
}
