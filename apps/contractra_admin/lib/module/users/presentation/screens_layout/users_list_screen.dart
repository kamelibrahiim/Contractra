import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_breakpoints.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/git_it.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_button.dart';
import '../../../../widget/app_text_field.dart';
import '../../../../widget/empty_state_widget.dart';
import '../../../../widget/error_state_widget.dart';
import '../../../../widget/loading_state_widget.dart';
import '../../cubit/users_list_cubit.dart';
import '../../data_layer/models/user_summary.dart';
import '../../data_layer/repository/users_repository.dart';
import '../widgets/user_row_card.dart';
import '../widgets/users_table.dart';

class UsersListScreen extends StatelessWidget {
  const UsersListScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (_) => UsersListCubit(GitIt.instance<UsersRepository>()),
      child: const _UsersListView(),
    );
  }
}

class _UsersListView extends StatelessWidget {
  const _UsersListView();

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final state = context.watch<UsersListCubit>().state;
    final cubit = context.read<UsersListCubit>();
    final isNarrow = MediaQuery.sizeOf(context).width < AppBreakpoints.medium;

    return Padding(
      padding: AppSpacing.allLg,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'navUsers'.tr,
                  style: TextStyleHelper.h3.copyWith(color: theme.textPrimaryColor),
                ),
              ),
              AppButton(
                key: const Key('users_new_user'),
                label: 'usersNewUser'.tr,
                leadingIcon: Icons.add,
                onPressed: () async {
                  await context.push('/users/new');
                  if (context.mounted) cubit.refresh();
                },
              ),
            ],
          ),
          SizedBox(height: AppSpacing.md),
          Row(
            children: [
              Expanded(
                child: AppTextField(
                  key: const Key('users_search'),
                  label: 'usersSearchLabel'.tr,
                  onChanged: cubit.search,
                  prefixIcon: Icons.search,
                ),
              ),
              SizedBox(width: AppSpacing.md),
              DropdownButton<UserAccountStatus?>(
                key: const Key('users_status_filter'),
                value: state.statusFilter,
                hint: Text('usersStatusFilterAll'.tr),
                onChanged: cubit.filterByStatus,
                items: [
                  DropdownMenuItem(value: null, child: Text('usersStatusFilterAll'.tr)),
                  DropdownMenuItem(
                    value: UserAccountStatus.active,
                    child: Text('userStatusActive'.tr),
                  ),
                  DropdownMenuItem(
                    value: UserAccountStatus.disabled,
                    child: Text('userStatusDisabled'.tr),
                  ),
                ],
              ),
            ],
          ),
          SizedBox(height: AppSpacing.lg),
          Expanded(child: _body(context, state, cubit, isNarrow)),
          if (state.status == UsersListStatus.loaded && state.items.isNotEmpty)
            _pagination(context, state, cubit),
        ],
      ),
    );
  }

  Widget _body(BuildContext context, UsersListState state, UsersListCubit cubit, bool isNarrow) {
    switch (state.status) {
      case UsersListStatus.loading:
        return LoadingStateWidget(label: 'usersLoading'.tr);
      case UsersListStatus.error:
        return ErrorStateWidget(
          title: 'usersLoadError'.tr,
          actionLabel: 'retry'.tr,
          onAction: cubit.refresh,
        );
      case UsersListStatus.loaded:
        if (state.items.isEmpty) {
          return EmptyStateWidget(
            icon: Icons.people_outline,
            title: 'usersEmptyTitle'.tr,
            message: 'usersEmptyMessage'.tr,
          );
        }
        return isNarrow
            ? ListView.separated(
                itemCount: state.items.length,
                separatorBuilder: (_, _) => SizedBox(height: AppSpacing.sm),
                itemBuilder: (_, i) => UserRowCard(
                  user: state.items[i],
                  onTap: () => context.push('/users/${state.items[i].id}'),
                ),
              )
            : UsersTable(
                users: state.items,
                onRowTap: (user) => context.push('/users/${user.id}'),
              );
    }
  }

  Widget _pagination(BuildContext context, UsersListState state, UsersListCubit cubit) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    return Padding(
      padding: EdgeInsets.only(top: AppSpacing.md),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          AppButton(
            label: 'previous'.tr,
            variant: AppButtonVariant.text,
            onPressed: state.page > 1 ? () => cubit.goToPage(state.page - 1) : null,
          ),
          Padding(
            padding: AppSpacing.horizontalMd,
            child: Text(
              'usersPageOf'.tr.replaceAll('{page}', '${state.page}').replaceAll('{total}', '${state.totalPages}'),
              style: TextStyleHelper.bodySm.copyWith(color: theme.textSecondaryColor),
            ),
          ),
          AppButton(
            label: 'next'.tr,
            variant: AppButtonVariant.text,
            onPressed: state.page < state.totalPages ? () => cubit.goToPage(state.page + 1) : null,
          ),
        ],
      ),
    );
  }
}
