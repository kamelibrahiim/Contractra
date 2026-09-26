import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_admin/core/language/locales.dart';
import 'package:contractra_admin/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';
import 'package:contractra_admin/module/users/presentation/screens_layout/user_details_screen.dart';
import 'package:contractra_admin/utilities/git_it.dart';

class _MockUsersRepository extends Mock implements UsersRepository {}

const _activeUser = UserSummary(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.active);
const _disabledUser = UserSummary(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.disabled);

void main() {
  late _MockUsersRepository repository;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    rootBundle.clear();
    repository = _MockUsersRepository();
    when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_activeUser));
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
          home: Scaffold(body: UserDetailsScreen(userId: 'u-1')),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(UserDetailsScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  group('UserDetailsScreen', () {
    testWidgets('renders identifier, roles, and status for an active user', (tester) async {
      await pumpScreen(tester);

      expect(find.text('admin'), findsOneWidget);
      expect(find.byKey(const Key('user_details_disable')), findsOneWidget);
      expect(find.byKey(const Key('user_details_reactivate')), findsNothing);
    });

    testWidgets('cancelling the disable confirmation does not disable', (tester) async {
      await pumpScreen(tester);

      await tester.tap(find.byKey(const Key('user_details_disable')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('confirm_dialog_cancel')));
      await tester.pumpAndSettle();

      verifyNever(() => repository.disable('u-1'));
      expect(find.byKey(const Key('user_details_disable')), findsOneWidget);
    });

    testWidgets('confirming disable calls the repository and updates the badge', (tester) async {
      when(() => repository.disable('u-1')).thenAnswer((_) async {
        when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_disabledUser));
        return const UsersSuccess(null);
      });
      await pumpScreen(tester);

      await tester.tap(find.byKey(const Key('user_details_disable')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('confirm_dialog_confirm')));
      await tester.pumpAndSettle();

      verify(() => repository.disable('u-1')).called(1);
      expect(find.byKey(const Key('user_details_reactivate')), findsOneWidget);
    });

    testWidgets('confirming reset-password calls the repository and shows the default-password dialog', (tester) async {
      when(() => repository.resetPassword('u-1')).thenAnswer((_) async => const UsersSuccess(null));
      await pumpScreen(tester);

      await tester.tap(find.byKey(const Key('user_details_reset_password')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('confirm_dialog_confirm')));
      await tester.pumpAndSettle();

      verify(() => repository.resetPassword('u-1')).called(1);
      expect(find.textContaining('12345678'), findsOneWidget);
    });

    testWidgets('there is no delete action anywhere on this screen (FR-AUTH-004)', (tester) async {
      await pumpScreen(tester);

      expect(find.textContaining('Delete'), findsNothing);
      expect(find.byIcon(Icons.delete), findsNothing);
      expect(find.byIcon(Icons.delete_outline), findsNothing);
    });
  });
}
