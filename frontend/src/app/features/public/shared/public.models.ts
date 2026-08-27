export interface PublicTechnology {
  id: string;
  name: string;
  category: string;
  iconKey?: string | null;
}

export interface PublicProfile {
  fullName: string;
  professionalTitle: string | null;
  secondaryTitle: string | null;
  heroHeadline: string | null;
  heroSummary: string | null;
  aboutMarkdown: string | null;
  email: string | null;
  location: string | null;
  university: string | null;
  major: string | null;
  availabilityStatus: string | null;
  profileImageUrl: string | null;
  cvUrl: string | null;
}

export interface PublicExperience {
  id: string;
  companyName: string;
  roleTitle: string;
  location: string | null;
  startDate: string;
  endDate: string | null;
  isCurrent: boolean;
  summary: string | null;
  responsibilitiesMarkdown: string | null;
  companyUrl: string | null;
  technologies: PublicTechnology[];
}

export interface PublicEducation {
  id: string;
  institution: string;
  degree: string | null;
  fieldOfStudy: string | null;
  startDate: string | null;
  endDate: string | null;
  description: string | null;
  location: string | null;
}

export interface PublicTraining {
  id: string;
  title: string;
  provider: string | null;
  description: string | null;
  startDate: string | null;
  endDate: string | null;
  credentialUrl: string | null;
}

export interface PublicCertificate {
  id: string;
  name: string;
  issuer: string | null;
  issuedAt: string | null;
  expiresAt: string | null;
  credentialId: string | null;
  credentialUrl: string | null;
  certificateUrl: string | null;
}

export interface PublicSkill {
  id: string;
  name: string;
  category: string;
  experienceLevel: 'USED' | 'LEARNING' | 'EXPLORING';
  description: string | null;
  technologyId: string | null;
}

export interface PublicJourneyItem {
  id: string;
  title: string;
  subtitle: string | null;
  description: string | null;
  occurredAt: string | null;
  iconKey: string | null;
}

export interface PublicSocialLink {
  id: string;
  platform: string;
  label: string | null;
  url: string;
  iconKey: string | null;
}

export interface PublicProjectListItem {
  id: string;
  slug: string;
  title: string;
  subtitle: string | null;
  shortDescription: string | null;
  role: string | null;
  status: string;
  featured: boolean;
  thumbnailUrl: string | null;
  technologies: PublicTechnology[];
}

export interface PublicProjectSection {
  id: string;
  sectionType: string;
  title: string | null;
  subtitle: string | null;
  contentMarkdown: string | null;
  content: unknown;
  displayOrder: number;
}

export interface PublicProjectMedia {
  id: string;
  role: string;
  url: string;
  altText: string | null;
  caption: string | null;
  displayOrder: number;
}

export interface PublicProjectDetail {
  id: string;
  slug: string;
  title: string;
  subtitle: string | null;
  shortDescription: string | null;
  overviewMarkdown: string | null;
  role: string | null;
  teamSize: number | null;
  startDate: string | null;
  endDate: string | null;
  status: string;
  githubUrl: string | null;
  liveUrl: string | null;
  thumbnailUrl: string | null;
  seo: { title: string | null; description: string | null };
  technologies: PublicTechnology[];
  sections: PublicProjectSection[];
  media: PublicProjectMedia[];
}

export interface PortfolioAggregate {
  profile: PublicProfile;
  experiences: PublicExperience[];
  featuredProjects: PublicProjectListItem[];
  skills: PublicSkill[];
  educations: PublicEducation[];
  trainings: PublicTraining[];
  certificates: PublicCertificate[];
  journey: PublicJourneyItem[];
  socialLinks: PublicSocialLink[];
}

export interface ContactRequest { name: string; email: string; subject: string | null; message: string; }
export interface ContactSubmission { id: string; status: string; }
