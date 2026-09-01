export interface ReorderItem { id: string; displayOrder: number; }
export interface MediaSummary { id: string; publicUrl: string; fileName: string; altText: string | null; }
export interface TechnologySummary { id: string; name: string; category: string; iconKey: string | null; }
export interface AdminProfile { id: string; fullName: string; professionalTitle: string | null; secondaryTitle: string | null; heroHeadline: string | null; heroSummary: string | null; aboutMarkdown: string | null; email: string | null; phone: string | null; location: string | null; university: string | null; major: string | null; availabilityStatus: string | null; profileImage: MediaSummary | null; cvMedia: MediaSummary | null; isPublished: boolean; updatedAt: string; }
export type ProfileRequest = Omit<AdminProfile, 'id'|'profileImage'|'cvMedia'|'updatedAt'> & { profileImageId: string | null; cvMediaId: string | null };
export interface Experience { id: string; companyName: string; roleTitle: string; location: string | null; startDate: string; endDate: string | null; isCurrent: boolean; summary: string | null; responsibilitiesMarkdown: string | null; companyUrl: string | null; displayOrder: number; isPublished: boolean; technologies: TechnologySummary[]; }
export type ExperienceRequest = Omit<Experience, 'id'|'technologies'> & { technologyIds: string[] };
export interface Education { id: string; institution: string; degree: string | null; fieldOfStudy: string | null; startDate: string | null; endDate: string | null; description: string | null; location: string | null; displayOrder: number; isPublished: boolean; }
export type EducationRequest = Omit<Education, 'id'>;
export interface Training { id: string; title: string; provider: string | null; description: string | null; startDate: string | null; endDate: string | null; credentialUrl: string | null; displayOrder: number; isPublished: boolean; }
export type TrainingRequest = Omit<Training, 'id'>;
export interface Certificate { id: string; name: string; issuer: string | null; issuedAt: string | null; expiresAt: string | null; credentialId: string | null; credentialUrl: string | null; certificateMediaId: string | null; displayOrder: number; isPublished: boolean; }
export type CertificateRequest = Omit<Certificate, 'id'>;
export interface Technology { id: string; name: string; category: string; iconKey: string | null; websiteUrl: string | null; displayOrder: number; isActive: boolean; updatedAt: string; }
export type TechnologyRequest = Omit<Technology, 'id'|'updatedAt'>;
export type SkillLevel = 'USED'|'LEARNING'|'EXPLORING';
export interface Skill { id: string; name: string; category: string; experienceLevel: SkillLevel; description: string | null; technologyId: string | null; displayOrder: number; isPublished: boolean; updatedAt: string; }
export type SkillRequest = Omit<Skill, 'id'|'updatedAt'>;
export interface JourneyItem { id: string; title: string; subtitle: string | null; description: string | null; occurredAt: string | null; iconKey: string | null; displayOrder: number; isPublished: boolean; updatedAt: string; }
export type JourneySourceType = 'MANUAL'|'EDUCATION'|'EXPERIENCE'|'PROJECT'|'TRAINING'|'CERTIFICATE';
export interface JourneyTimelineItem { id: string; title: string; subtitle: string | null; description: string | null; occurredAt: string | null; iconKey: string | null; sourceType: JourneySourceType; sourceId: string; isManual: boolean; startAt: string | null; endAt: string | null; isOngoing: boolean; timelineKind: 'PERIOD'|'POINT'; }
export type JourneyRequest = Omit<JourneyItem, 'id'|'updatedAt'>;
export interface SocialLink { id: string; platform: string; label: string | null; url: string; iconKey: string | null; displayOrder: number; isVisible: boolean; updatedAt: string; }
export type SocialLinkRequest = Omit<SocialLink, 'id'|'updatedAt'>;
export interface SiteSettings { siteName: string; footerText: string | null; showAvailability: boolean; showDownloadCv: boolean; showJourney: boolean; showAiAgent: boolean; defaultSeoTitle: string | null; defaultSeoDescription: string | null; }
export interface KnowledgeCounts { indexed: number; pending: number; failed: number; }
export interface RecentUpdate { resourceType: string; id: string; title: string; updatedAt: string; }
export interface Dashboard { projects: number; experiences: number; skills: number; certificates: number; knowledge: KnowledgeCounts; conversations: number; recentUpdates: RecentUpdate[]; }
export interface ProjectListItem { id: string; slug: string; title: string; role: string | null; status: ProjectStatus; featured: boolean; isPublished: boolean; displayOrder: number; thumbnailUrl: string | null; updatedAt: string; }
export type ProjectStatus = 'PLANNED'|'IN_PROGRESS'|'ACTIVE'|'COMPLETED'|'ARCHIVED';
export interface ProjectTechnology { technologyId: string; name: string; category: string; iconKey: string | null; displayOrder: number; }
export interface ProjectSection { id: string; sectionType: string; title: string | null; subtitle: string | null; contentMarkdown: string | null; content: unknown; displayOrder: number; isVisible: boolean; }
export interface ProjectMedia { id: string; mediaAssetId: string; mediaRole: string; url: string; altText: string | null; caption: string | null; displayOrder: number; }
export interface Project { id: string; slug: string; title: string; subtitle: string | null; shortDescription: string | null; overviewMarkdown: string | null; role: string | null; teamSize: number | null; startDate: string | null; endDate: string | null; status: ProjectStatus; githubUrl: string | null; liveUrl: string | null; thumbnailMediaId: string | null; thumbnailUrl: string | null; featured: boolean; isPublished: boolean; displayOrder: number; seoTitle: string | null; seoDescription: string | null; updatedAt: string; technologies: ProjectTechnology[]; sections: ProjectSection[]; media: ProjectMedia[]; }
export type ProjectRequest = Omit<Project, 'id'|'thumbnailUrl'|'updatedAt'|'technologies'|'sections'|'media'> & { technologyIds: string[] };
export interface ProjectSectionRequest { sectionType: string; title: string | null; subtitle: string | null; contentMarkdown: string | null; content: unknown; displayOrder: number; isVisible: boolean; }
