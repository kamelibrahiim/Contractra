import 'package:flutter/material.dart';

import '../../../../../core/theme/app_spacing.dart';

/// Phone layout: full-width form edge to edge within the side gutter.
class SmallLoginScreen extends StatelessWidget {
  const SmallLoginScreen({super.key, required this.form});

  final Widget form;

  @override
  Widget build(BuildContext context) {
    return Padding(padding: AppSpacing.horizontalLg, child: form);
  }
}
