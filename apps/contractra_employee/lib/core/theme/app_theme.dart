import 'package:flutter/widgets.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import 'cubit/theme_cubit.dart';
import 'theme_model.dart';

export 'cubit/theme_cubit.dart';
export 'theme_model.dart';

ThemeModel appTheme(BuildContext context) =>
    context.read<ThemeCubit>().state.appTheme;
