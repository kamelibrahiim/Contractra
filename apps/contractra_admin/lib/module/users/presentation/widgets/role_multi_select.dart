import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../data_layer/models/role_option.dart';

/// A plain checkbox list — there is no confirmed catalog UI convention to
/// match, and the role count is small (4 in the mock; `GET /api/roles`
/// drives the real count).
class RoleMultiSelect extends StatelessWidget {
  const RoleMultiSelect({
    super.key,
    required this.label,
    required this.roles,
    required this.selectedCodes,
    required this.onToggle,
  });

  final String label;
  final List<RoleOption> roles;
  final Set<String> selectedCodes;
  final void Function(String code, bool selected) onToggle;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(label, style: TextStyleHelper.label.copyWith(color: theme.textPrimaryColor)),
        SizedBox(height: AppSpacing.xs),
        for (final role in roles)
          CheckboxListTile(
            key: Key('role_option_${role.code}'),
            value: selectedCodes.contains(role.code),
            onChanged: (checked) => onToggle(role.code, checked ?? false),
            title: Text(
              role.displayName,
              style: TextStyleHelper.bodyMd.copyWith(color: theme.textPrimaryColor),
            ),
            controlAffinity: ListTileControlAffinity.leading,
            contentPadding: EdgeInsets.zero,
            dense: true,
          ),
      ],
    );
  }
}
