import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

import 'core/font/app_font.dart';
import 'core/language/app_language.dart' as app_language;
import 'core/language/locales.dart';
import 'core/theme/app_theme.dart';
import 'utilities/enum.dart';
import 'utilities/git_it.dart';
import 'utilities/router_config.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await GitIt.initGitIt();
  runApp(
    MultiBlocProvider(
      providers: [
        BlocProvider(create: (_) => app_language.LanguageCubit()),
        BlocProvider(create: (_) => ThemeCubit()),
        BlocProvider(create: (_) => FontCubit()),
      ],
      child: const EntryPoint(),
    ),
  );
}

class EntryPoint extends StatelessWidget {
  const EntryPoint({super.key});

  @override
  Widget build(BuildContext context) {
    final languageState = context.watch<app_language.LanguageCubit>().state;
    final themeCubit = context.watch<ThemeCubit>();
    final fontState = context.watch<FontCubit>().state;

    return LayoutBuilder(
      builder: (context, constraints) {
        return ScreenUtilPlusInit(
          designSize: const Size(1440, 1088),
          builder: (_, child) => MaterialApp.router(
            scrollBehavior: AppScrollBehavior(),
            routerConfig: GoRouterConfig.router,
            debugShowCheckedModeBanner: false,
            title: 'Contractra Admin',
            locale: Locale(languageState.appLang.name),
            theme: themeCubit.appThemeMode,
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(fontState.fontSizeScale)),
              child: child ?? const SizedBox.shrink(),
            ),
            supportedLocales: Languages.values.map((e) => Locale(e.name)).toList(),
            localizationsDelegates: const [
              AppLocalizationsDelegate(),
              GlobalMaterialLocalizations.delegate,
              GlobalWidgetsLocalizations.delegate,
              GlobalCupertinoLocalizations.delegate,
            ],
          ),
        );
      },
    );
  }
}

class AppScrollBehavior extends MaterialScrollBehavior {
  @override
  Set<PointerDeviceKind> get dragDevices => {
        PointerDeviceKind.touch,
        PointerDeviceKind.mouse,
      };
}
