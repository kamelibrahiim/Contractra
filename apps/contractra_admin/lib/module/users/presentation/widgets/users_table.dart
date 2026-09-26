import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../data_layer/models/user_summary.dart';
import 'user_status_badge.dart';

/// >=600px layout: a plain table, one row per user.
class UsersTable extends StatelessWidget {
  const UsersTable({super.key, required this.users, this.onRowTap});

  final List<UserSummary> users;
  final void Function(UserSummary user)? onRowTap;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final headerStyle = TextStyleHelper.label.copyWith(color: theme.textSecondaryColor);
    final cellStyle = TextStyleHelper.bodyMd.copyWith(color: theme.textPrimaryColor);

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: DataTable(
        headingRowColor: WidgetStatePropertyAll(theme.surfaceColor),
        columns: [
          DataColumn(label: Text('usersColumnIdentifier'.tr, style: headerStyle)),
          DataColumn(label: Text('usersColumnRoles'.tr, style: headerStyle)),
          DataColumn(label: Text('usersColumnStatus'.tr, style: headerStyle)),
        ],
        rows: [
          for (final user in users)
            DataRow(
              onSelectChanged: onRowTap == null ? null : (_) => onRowTap!(user),
              cells: [
                DataCell(Text(user.loginIdentifier, style: cellStyle)),
                DataCell(Text(user.roles.join(', '), style: cellStyle)),
                DataCell(UserStatusBadge(status: user.status)),
              ],
            ),
        ],
      ),
    );
  }
}
