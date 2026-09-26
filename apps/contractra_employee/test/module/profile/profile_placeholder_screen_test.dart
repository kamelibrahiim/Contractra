import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:contractra_employee/core/language/locales.dart';
import 'package:contractra_employee/core/session/session_cubit.dart';
import 'package:contractra_employee/core/theme/cubit/theme_cubit.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_employee/module/profile/presentation/screens_layout/profile_placeholder_screen.dart';
import 'package:contractra_employee/utilities/git_it.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

void main() {
  late _MockAuthRepository authRepository;
  late SessionCubit sessionCubit;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    await GitIt.initGitIt();
    rootBundle.clear();
    authRepository = _MockAuthRepository();
    when(() => authRepository.logout()).thenAnswer((_) async {});
    sessionCubit = SessionCubit(authRepository);
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
          home: Scaffold(body: ProfilePlaceholderScreen()),
        ),
      ),
    ));
    for (var i = 0; i < 100 && find.byType(ProfilePlaceholderScreen).evaluate().isEmpty; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  group('ProfilePlaceholderScreen', () {
    testWidgets('renders the coming-soon message and a logout button', (tester) async {
      await pumpScreen(tester);

      expect(find.text('Profile'), findsOneWidget);
      expect(find.byKey(const Key('profile_logout')), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets('tapping logout calls SessionCubit.logout', (tester) async {
      await pumpScreen(tester);

      await tester.tap(find.byKey(const Key('profile_logout')));
      await tester.pump();

      expect(sessionCubit.state, const SessionUnauthenticated());
      verify(() => authRepository.logout()).called(1);
    });
  });
}
