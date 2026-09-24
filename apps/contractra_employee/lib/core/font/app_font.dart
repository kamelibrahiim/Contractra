import 'package:flutter/widgets.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../utilities/enum.dart';
import 'cubit/font_cubit.dart';

export 'cubit/font_cubit.dart';

FontFamilyTypes appFontFamily(BuildContext context) =>
    context.read<FontCubit>().state.fontFamily;

double appFontSizeScale(BuildContext context) =>
    context.read<FontCubit>().state.fontSizeScale;
