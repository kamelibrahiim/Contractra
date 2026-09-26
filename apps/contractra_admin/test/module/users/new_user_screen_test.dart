import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/language/locales.dart';
import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/module/users/data_layer/models/role_option.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';
import 'package:contractra_admin/module/users/presentation/screens_layout/new_user_screen.dart';
import 'package:contractra_admin/utilities/git_it.dart';

class _MockUsersRepository extends Mock implements UsersRepository {}

const _roles = [
  RoleOption(code: 'Admin', displayName: 'Admin'),
  RoleOption(code: 'Engineer', displayName: 'Engineer'),
];

void main() {
  late _MockUsersRepository repository;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    rootBundle.clear();
    repository = _MockUsersRepository();
    when(() => repository.listRoles()).thenAnswer((_) async => const UsersSuccess(_roles));
    GitIt.instance.registerLazySingleton<UsersRepository>(() => repository);
  });

  Future<void> pumpScreen(WidgetTester tester) async {
    await tester.pumpWidget(ScreenUtilPlusInit(
      designSize: const Size(1440, 1088),
      builder: (_, _) => BlocProvider(
        create: (_) => ThemeCubit(),
        child: const MaterialApp(
          locale: Locale('en'),
          localizationsDelegates: [
            AppLocalizationsDelegate(),
            DefaultMaterialLocalizations.delegate,
            DefaultWidgetsLocalizations.delegate,
          ],
          home: Scaffold(body: NewUserScreen()),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(NewUserScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  group('NewUserScreen', () {
    testWidgets('submitting with no role selected shows the role-required message', (tester) async {
      await pumpScreen(tester);

      await tester.enterText(
        find.descendant(of: find.byKey(const Key('new_user_identifier')), matching: find.byType(EditableText)),
        'new.engineer',
      );
      await tester.tap(find.byKey(const Key('new_user_submit')));
      await tester.pump();

      expect(find.text('Select at least one role.'), findsOneWidget);
    });

    testWidgets('a taken identifier shows the server error', (tester) async {
      when(() => repository.create(loginIdentifier: 'admin', roleCodes: ['Engineer']))
          .thenAnswer((_) async => const UsersError(LoginIdentifierTakenFailure()));
      await pumpScreen(tester);

      await tester.enterText(
        find.descendant(of: find.byKey(const Key('new_user_identifier')), matching: find.byType(EditableText)),
        'admin',
      );
      await tester.tap(find.byKey(const Key('role_option_Engineer')));
      await tester.tap(find.byKey(const Key('new_user_submit')));
      await tester.pump();
      await tester.pump();

      expect(find.text('This identifier is already in use.'), findsOneWidget);
    });

    testWidgets('success shows the default-password dialog', (tester) async {
      when(() => repository.create(loginIdentifier: 'new.engineer', roleCodes: ['Engineer'])).thenAnswer(
        (_) async => const UsersSuccess(
          UserSummary(id: 'u-9', loginIdentifier: 'new.engineer', roles: ['Engineer'], status: UserAccountStatus.active),
        ),
      );
      await pumpScreen(tester);

      await tester.enterText(
        find.descendant(of: find.byKey(const Key('new_user_identifier')), matching: find.byType(EditableText)),
        'new.engineer',
      );
      await tester.tap(find.byKey(const Key('role_option_Engineer')));
      await tester.tap(find.byKey(const Key('new_user_submit')));
      await tester.pumpAndSettle();

      expect(find.textContaining('12345678'), findsOneWidget);
    });
  });
}
