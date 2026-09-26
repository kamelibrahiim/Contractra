import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/theme/app_radius.dart';
import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';
import '../utilities/text_style_helper.dart';

/// The Employee app's single reusable text input.
///
/// Built on [TextFormField] (not a raw [TextField]) so every future form —
/// expenses, petty cash, labor entries, assignments, profile/auth — gets
/// validation for free. Colors come from [ThemeModel], spacing from
/// [AppSpacing], radius from [AppRadius], and typography from
/// [TextStyleHelper]; no palette, spacing, or font values are duplicated
/// here, and no business validators or auth-specific behavior (e.g.
/// password-visibility toggling) live in this generic component.
class AppTextField extends StatelessWidget {
  const AppTextField({
    super.key,
    required this.label,
    this.hint,
    this.controller,
    this.initialValue,
    this.validator,
    this.onChanged,
    this.onFieldSubmitted,
    this.keyboardType,
    this.textInputAction,
    this.prefixIcon,
    this.suffixIcon,
    this.onSuffixIconPressed,
    this.errorText,
    this.obscureText = false,
    this.enabled = true,
    this.readOnly = false,
    this.maxLines = 1,
    this.minLines,
    this.maxLength,
    this.autofocus = false,
    this.focusNode,
  }) : assert(
          controller == null || initialValue == null,
          'AppTextField cannot take both a controller and an initialValue.',
        ),
        assert(
          !obscureText || (maxLines == 1 && (minLines == null || minLines == 1)),
          'AppTextField cannot combine obscureText with multiple lines.',
        );

  final String label;
  final String? hint;
  final TextEditingController? controller;
  final String? initialValue;
  final FormFieldValidator<String>? validator;
  final ValueChanged<String>? onChanged;
  final ValueChanged<String>? onFieldSubmitted;
  final TextInputType? keyboardType;
  final TextInputAction? textInputAction;
  final IconData? prefixIcon;
  final IconData? suffixIcon;

  /// Makes [suffixIcon] tappable; what the tap does is the caller's concern.
  final VoidCallback? onSuffixIconPressed;

  /// Caller-driven error shown independently of [validator].
  final String? errorText;
  final bool obscureText;
  final bool enabled;
  final bool readOnly;
  final int? maxLines;
  final int? minLines;
  final int? maxLength;
  final bool autofocus;
  final FocusNode? focusNode;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          label,
          style: TextStyleHelper.label.copyWith(color: theme.textPrimaryColor),
        ),
        SizedBox(height: AppSpacing.xs),
        TextFormField(
          controller: controller,
          initialValue: initialValue,
          validator: validator,
          onChanged: onChanged,
          onFieldSubmitted: onFieldSubmitted,
          keyboardType: keyboardType,
          textInputAction: textInputAction,
          obscureText: obscureText,
          enabled: enabled,
          readOnly: readOnly,
          maxLines: maxLines,
          minLines: minLines,
          maxLength: maxLength,
          autofocus: autofocus,
          focusNode: focusNode,
          style: TextStyleHelper.bodyMd.copyWith(
            color: enabled ? theme.textPrimaryColor : theme.disabledColor,
          ),
          cursorColor: theme.primaryColor,
          decoration: InputDecoration(
            hintText: hint,
            errorText: errorText,
            hintStyle: TextStyleHelper.bodyMd.copyWith(
              color: theme.textSecondaryColor,
            ),
            errorStyle: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
            errorMaxLines: 2,
            prefixIcon: prefixIcon == null
                ? null
                : Icon(prefixIcon, color: theme.textSecondaryColor),
            suffixIcon: suffixIcon == null
                ? null
                : onSuffixIconPressed == null
                    ? Icon(suffixIcon, color: theme.textSecondaryColor)
                    : IconButton(
                        icon: Icon(suffixIcon, color: theme.textSecondaryColor),
                        onPressed: enabled ? onSuffixIconPressed : null,
                      ),
            filled: true,
            fillColor: readOnly ? theme.backgroundColor : theme.surfaceColor,
            contentPadding: AppSpacing.allMd,
            constraints: const BoxConstraints(minHeight: 48),
            border: _border(theme.borderColor),
            enabledBorder: _border(theme.borderColor),
            focusedBorder: _border(theme.primaryColor, width: 2),
            disabledBorder: _border(theme.disabledColor),
            errorBorder: _border(theme.dangerColor),
            focusedErrorBorder: _border(theme.dangerColor, width: 2),
          ),
        ),
      ],
    );
  }

  static OutlineInputBorder _border(Color color, {double width = 1}) =>
      OutlineInputBorder(
        borderRadius: AppRadius.mdRadius,
        borderSide: BorderSide(color: color, width: width),
      );
}
