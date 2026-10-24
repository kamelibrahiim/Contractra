import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_radius.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_card.dart';
import '../../data_layer/models/party_summary.dart';

Color partyStatusColor(ThemeModel theme, PartyStatus status) => switch (status) {
      PartyStatus.active => theme.successColor,
      PartyStatus.needsClassification => theme.warningColor,
      PartyStatus.inactive => theme.disabledColor,
    };

String partyStatusLabelKey(PartyStatus status) => switch (status) {
      PartyStatus.active => 'partyStatusActive',
      PartyStatus.needsClassification => 'partyStatusNeedsClassification',
      PartyStatus.inactive => 'partyStatusInactive',
    };

class PartyListItemWidget extends StatelessWidget {
  const PartyListItemWidget({super.key, required this.party});

  final PartySummary party;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final statusColor = partyStatusColor(theme, party.status);

    return AppCard(
      padding: AppSpacing.allMd,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  party.displayName,
                  style: TextStyleHelper.bodyLg.copyWith(
                    color: theme.textPrimaryColor,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                SizedBox(height: AppSpacing.xs),
                if (party.roleCodes.isEmpty)
                  Text(
                    'partyNoRoles'.tr,
                    style: TextStyleHelper.bodySm.copyWith(color: theme.textSecondaryColor),
                  )
                else
                  Wrap(
                    spacing: AppSpacing.xs,
                    runSpacing: AppSpacing.xs,
                    children: [
                      for (final code in party.roleCodes)
                        _Badge(
                          label: 'partyRole$code'.tr == 'partyRole$code' ? code : 'partyRole$code'.tr,
                          color: theme.primaryColor,
                        ),
                    ],
                  ),
              ],
            ),
          ),
          SizedBox(width: AppSpacing.md),
          _Badge(
            key: const Key('party_status'),
            label: partyStatusLabelKey(party.status).tr,
            color: statusColor,
            dot: true,
          ),
        ],
      ),
    );
  }
}

class _Badge extends StatelessWidget {
  const _Badge({super.key, required this.label, required this.color, this.dot = false});

  final String label;
  final Color color;
  final bool dot;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: AppRadius.pillRadius,
      ),
      child: Padding(
        padding: EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: 2),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (dot) ...[
              Container(
                width: 8,
                height: 8,
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
              ),
              SizedBox(width: AppSpacing.xs),
            ],
            Text(label, style: TextStyleHelper.caption.copyWith(color: color, fontWeight: FontWeight.w600)),
          ],
        ),
      ),
    );
  }
}
