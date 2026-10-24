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
import 'package:contractra_admin/module/users/data_layer/models/user_page.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';
import 'package:contractra_admin/module/users/presentation/screens_layout/users_list_screen.dart';
import 'package:contractra_admin/module/users/presentation/widgets/users_table.dart';
import 'package:contractra_admin/module/users/presentation/widgets/user_row_card.dart';
import 'package:contractra_admin/utilities/git_it.dart';
import 'package:contractra_admin/widget/empty_state_widget.dart';
import 'package:contractra_admin/widget/error_state_widget.dart';
import 'package:contractra_admin/widget/loading_state_widget.dart';

class _MockUsersRepository extends Mock implements UsersRepository {}

const _users = [
  UserSummary(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.active),
  UserSummary(id: 'u-2', loginIdentifier: 'disabled.user', roles: ['Engineer'], status: UserAccountStatus.disabled),
];

void main() {
  late _MockUsersRepository repository;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    rootBundle.clear();
    repository = _MockUsersRepository();
    GitIt.instance.registerLazySingleton<UsersRepository>(() => repository);
  });

  Future<void> pumpScreen(WidgetTester tester, {Size size = const Size(1000, 900)}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
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
          home: Scaffold(body: UsersListScreen()),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(UsersListScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
  }

  group('UsersListScreen', () {
    testWidgets('shows a loading state, then the table on success', (tester) async {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null)).thenAnswer(
        (_) => Future.delayed(
          const Duration(milliseconds: 200),
          () => const UsersSuccess(UserPage(items: _users, totalCount: 2)),
        ),
      );

      await pumpScreen(tester);
      await tester.pump();
      expect(find.byType(LoadingStateWidget), findsOneWidget);

      await tester.pump(const Duration(milliseconds: 300));
      expect(find.byType(UsersTable), findsOneWidget);
      expect(find.text('admin'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('narrow width shows a card list instead of the table', (tester) async {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersSuccess(UserPage(items: _users, totalCount: 2)));

      await pumpScreen(tester, size: const Size(400, 900));
      await tester.pumpAndSettle();

      expect(find.byType(UsersTable), findsNothing);
      expect(find.byType(UserRowCard), findsNWidgets(2));
    });

    testWidgets('an empty result shows EmptyStateWidget', (tester) async {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersSuccess(UserPage(items: [], totalCount: 0)));

      await pumpScreen(tester);
      await tester.pumpAndSettle();

      expect(find.byType(EmptyStateWidget), findsOneWidget);
    });

    testWidgets('a repository error shows ErrorStateWidget with a retry action', (tester) async {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersError(NetworkFailure()));

      await pumpScreen(tester);
      await tester.pumpAndSettle();

      expect(find.byType(ErrorStateWidget), findsOneWidget);

      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersSuccess(UserPage(items: _users, totalCount: 2)));
      await tester.tap(find.text('Retry'));
      await tester.pumpAndSettle();

      expect(find.byType(UsersTable), findsOneWidget);
    });
  });
}
