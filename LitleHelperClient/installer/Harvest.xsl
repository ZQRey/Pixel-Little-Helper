<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:w="http://schemas.microsoft.com/wix/2006/wi">
  <xsl:output method="xml" indent="yes" />
  <xsl:key name="main" match="w:Component[w:File[contains(@Source, '\PixelHelper.exe')]]" use="@Id" />
  <xsl:template match="@*|node()"><xsl:copy><xsl:apply-templates select="@*|node()" /></xsl:copy></xsl:template>
  <xsl:template match="w:Component[key('main', @Id)]|w:ComponentRef[key('main', @Id)]" />
</xsl:stylesheet>
